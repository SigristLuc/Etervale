using System.Buffers;
using System.Net;
using System.Net.Sockets;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Transport;

public sealed record PristonConnectionOptions
{
    public byte Xor { get; init; } = 0xED;

    // Bytes waiting for the socket before the peer is treated as too slow and disconnected.
    public int MaxPendingSendBytes { get; init; } = 512 * 1024;
}

// One Priston TCP stream: frames are {int size, uint code}, at most 8192 bytes. Packets whose code high word is 0x8001
// are encrypted envelopes checked by the replay window; every other code is plaintext.
// Sends from any thread are appended to one pending buffer and flushed by a single writer, so packets queued while a
// socket write is in flight leave together in the next write.
public sealed class PristonConnection : IPacketSink, IAsyncDisposable
{
    // The original only encrypts below the 8192-byte frame size - 16 bytes; larger packets go out in plaintext.
    public const int MaxEncryptedPlaintext = PristonFrameCodec.MaxFrameSize - 16;
    private const uint EnvelopeMask = 0xFFFF0000;
    private const int ReceiveBufferSize = PristonFrameCodec.MaxFrameSize * 2;

    private readonly Socket _socket;
    private readonly PristonConnectionOptions _options;
    private readonly PristonReplayWindow _replay = new();
    private readonly object _sendGate = new();
    private readonly SemaphoreSlim _sendSignal = new(0, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly byte _keyHigh;
    private byte[] _pending;
    private int _pendingLength;
    private bool _signalled;
    private bool _closed;
    private ushort _sendCounter = 0x0010;
    private long _bytesSent;
    private long _bytesReceived;

    public PristonConnection(Socket socket, PristonConnectionOptions? options = null)
    {
        _socket = socket;
        _socket.NoDelay = true;
        _options = options ?? new PristonConnectionOptions();
        _pending = ArrayPool<byte>.Shared.Rent(PristonFrameCodec.MaxFrameSize * 4);
        RemoteEndPoint = socket.RemoteEndPoint;
        LocalEndPoint = socket.LocalEndPoint;
        _keyHigh = RemoteEndPoint is IPEndPoint { Address: var address } && address.MapToIPv4().GetAddressBytes() is { Length: 4 } octets
            ? octets[0]
            : (byte)0;
    }

    // Captured at construction: Socket throws ObjectDisposedException for these after Close.
    public EndPoint? RemoteEndPoint { get; }

    public EndPoint? LocalEndPoint { get; }

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public bool IsClosed => Volatile.Read(ref _closed);

    public string? CloseReason { get; private set; }

    public CancellationToken Closed => _lifetime.Token;

    // Runs the receive and send loops until the peer closes, a protocol error occurs or Close is called.
    // The packet memory passed to the handler is valid only until the returned ValueTask completes.
    public async Task RunAsync(Func<ReadOnlyMemory<byte>, ValueTask> onPacket, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static state => ((PristonConnection)state!).Close("cancelled"), this);
        var sender = SendLoopAsync();
        try
        {
            await ReceiveLoopAsync(onPacket);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            Close(ex.Message);
        }
        catch (InvalidDataException ex)
        {
            Close("protocol: " + ex.Message);
        }
        finally
        {
            Close("receive ended");
            await sender;
        }
    }

    private async Task ReceiveLoopAsync(Func<ReadOnlyMemory<byte>, ValueTask> onPacket)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        var decoded = ArrayPool<byte>.Shared.Rent(PristonFrameCodec.MaxFrameSize);
        try
        {
            var filled = 0;
            while (!IsClosed)
            {
                var read = await _socket.ReceiveAsync(buffer.AsMemory(filled, ReceiveBufferSize - filled), SocketFlags.None, _lifetime.Token);
                if (read == 0)
                {
                    Close("peer closed");
                    return;
                }

                Interlocked.Add(ref _bytesReceived, read);
                filled += read;
                var offset = 0;
                while (filled - offset >= PristonFrameCodec.HeaderSize)
                {
                    var status = PristonFrameCodec.ReadHeader(buffer.AsSpan(offset, filled - offset), out var size, out var code);
                    if (status == PristonHeaderStatus.Invalid)
                    {
                        throw new InvalidDataException("invalid frame header");
                    }

                    if (filled - offset < size)
                    {
                        break;
                    }

                    var frame = buffer.AsMemory(offset, size);
                    offset += size;
                    if ((code & EnvelopeMask) == PristonFrameCodec.EncodedPacketCode)
                    {
                        if (!PristonPacketCipher.TryDecode(frame.Span, decoded, _options.Xor, out var counter, out var length))
                        {
                            throw new InvalidDataException("encrypted packet failed its checksum");
                        }

                        if (!_replay.TryAccept(counter))
                        {
                            continue;
                        }

                        await onPacket(decoded.AsMemory(0, length));
                    }
                    else if ((code & EnvelopeMask) == 0x90010000)
                    {
                        throw new InvalidDataException("The 0x9001xxxx envelope requires the server-pushed decoder and is not supported");
                    }
                    else
                    {
                        await onPacket(frame);
                    }

                    if (IsClosed)
                    {
                        return;
                    }
                }

                if (offset > 0)
                {
                    buffer.AsSpan(offset, filled - offset).CopyTo(buffer);
                    filled -= offset;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(decoded);
        }
    }

    public bool Send(ReadOnlySpan<byte> packet) => Enqueue(packet, encrypt: false);

    // Encrypted envelope with the session XOR when the packet fits, otherwise plaintext.
    public bool SendEncrypted(ReadOnlySpan<byte> packet) => Enqueue(packet, encrypt: packet.Length < MaxEncryptedPlaintext);

    private bool Enqueue(ReadOnlySpan<byte> packet, bool encrypt)
    {
        if (PristonFrameCodec.ReadHeader(packet, out var size, out _) != PristonHeaderStatus.Complete || size != packet.Length)
        {
            throw new ArgumentException("Expected one complete Priston packet.", nameof(packet));
        }

        var wireLength = encrypt ? packet.Length + PristonPacketCipher.Overhead : packet.Length;
        bool signal;
        lock (_sendGate)
        {
            if (_closed)
            {
                return false;
            }

            if (_pendingLength + wireLength > _options.MaxPendingSendBytes)
            {
                _pendingLength = 0;
                CloseLocked("send backlog exceeded " + _options.MaxPendingSendBytes + " bytes");
                return false;
            }

            EnsureCapacity(_pendingLength + wireLength);
            var destination = _pending.AsSpan(_pendingLength, wireLength);
            if (encrypt)
            {
                var seed = (ushort)(_keyHigh << 8 | Random.Shared.Next(255));
                PristonPacketCipher.Encode(packet, destination, seed, _sendCounter, _options.Xor);
                _sendCounter++;
            }
            else
            {
                packet.CopyTo(destination);
            }

            _pendingLength += wireLength;
            signal = !_signalled;
            _signalled = true;
        }

        if (signal)
        {
            _sendSignal.Release();
        }

        return true;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _pending.Length)
        {
            return;
        }

        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(required, _pending.Length * 2));
        _pending.AsSpan(0, _pendingLength).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_pending);
        _pending = larger;
    }

    private async Task SendLoopAsync()
    {
        var writing = ArrayPool<byte>.Shared.Rent(_pending.Length);
        try
        {
            while (true)
            {
                try
                {
                    await _sendSignal.WaitAsync(_lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                int length;
                lock (_sendGate)
                {
                    _signalled = false;
                    length = _pendingLength;
                    if (writing.Length < length)
                    {
                        ArrayPool<byte>.Shared.Return(writing);
                        writing = ArrayPool<byte>.Shared.Rent(length);
                    }

                    _pending.AsSpan(0, length).CopyTo(writing);
                    _pendingLength = 0;
                }

                var offset = 0;
                while (offset < length)
                {
                    var sent = await _socket.SendAsync(writing.AsMemory(offset, length - offset), SocketFlags.None, _lifetime.Token);
                    if (sent <= 0)
                    {
                        Close("send failed");
                        return;
                    }

                    offset += sent;
                }

                Interlocked.Add(ref _bytesSent, length);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            Close(ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(writing);
        }
    }

    // Lets already-queued packets (for example a FAILCONNECT reply) reach the socket before closing.
    public async Task FlushAndCloseAsync(string reason, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !IsClosed)
        {
            lock (_sendGate)
            {
                if (_pendingLength == 0 && !_signalled)
                {
                    break;
                }
            }

            await Task.Delay(10);
        }

        Close(reason);
    }

    public void Close(string reason)
    {
        lock (_sendGate)
        {
            CloseLocked(reason);
        }
    }

    private void CloseLocked(string reason)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        CloseReason = reason;
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
        }

        _socket.Close();
        _lifetime.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        Close("disposed");
        _lifetime.Dispose();
        _sendSignal.Dispose();
        lock (_sendGate)
        {
            ArrayPool<byte>.Shared.Return(_pending);
            _pending = [];
        }

        return ValueTask.CompletedTask;
    }
}
