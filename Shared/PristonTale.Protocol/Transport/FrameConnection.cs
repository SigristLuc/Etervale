using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Transport;

public sealed record FrameConnectionOptions
{
    // Bytes waiting for the stream before the peer is treated as too slow and disconnected.
    public int MaxPendingSendBytes { get; init; } = 512 * 1024;
}

// One connection: frames {int32 Size, uint32 Code, body} of at most 8192 bytes over an authenticated TLS stream.
// Sends from any thread are appended to one pending buffer and flushed by a single writer, so packets queued while a
// write is in flight leave together in the next write (one TLS record batch).
public sealed class FrameConnection : IPacketSink, IAsyncDisposable
{
    private const int ReceiveBufferSize = FrameCodec.MaxFrameSize * 2;

    private readonly Socket _socket;
    private readonly Stream _stream;
    private readonly FrameConnectionOptions _options;
    private readonly object _sendGate = new();
    // Wakes the single writer; waiting on it allocates nothing, and closing completes it.
    private readonly Channel<bool> _sendSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });
    private readonly CancellationTokenSource _lifetime = new();
    private byte[] _pending;
    private int _pendingLength;
    private bool _signalled;
    private bool _closed;
    private long _bytesSent;
    private long _bytesReceived;

    // The stream is the socket's authenticated TLS stream; the connection owns both.
    public FrameConnection(Socket socket, Stream stream, FrameConnectionOptions? options = null)
    {
        _socket = socket;
        _socket.NoDelay = true;
        _stream = stream;
        _options = options ?? new FrameConnectionOptions();
        _pending = ArrayPool<byte>.Shared.Rent(FrameCodec.MaxFrameSize * 4);
        RemoteEndPoint = socket.RemoteEndPoint;
        LocalEndPoint = socket.LocalEndPoint;
    }

    // Captured at construction: Socket throws ObjectDisposedException for these after Close.
    public EndPoint? RemoteEndPoint { get; }

    public EndPoint? LocalEndPoint { get; }

    // Frame bytes, before TLS framing.
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public bool IsClosed => Volatile.Read(ref _closed);

    public string? CloseReason { get; private set; }

    public CancellationToken Closed => _lifetime.Token;

    // Runs the receive and send loops until the peer closes, a protocol error occurs or Close is called.
    // The packet memory passed to the handler is valid only until the returned ValueTask completes.
    public async Task RunAsync(Func<ReadOnlyMemory<byte>, ValueTask> onPacket, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static state => ((FrameConnection)state!).Close("cancelled"), this);
        var sender = SendLoopAsync();
        try
        {
            await ReceiveLoopAsync(onPacket);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
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
        try
        {
            var filled = 0;
            while (!IsClosed)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(filled, ReceiveBufferSize - filled), _lifetime.Token);
                if (read == 0)
                {
                    Close("peer closed");
                    return;
                }

                Interlocked.Add(ref _bytesReceived, read);
                filled += read;
                var offset = 0;
                while (filled - offset >= FrameCodec.HeaderSize)
                {
                    var status = FrameCodec.ReadHeader(buffer.AsSpan(offset, filled - offset), out var size, out _);
                    if (status == FrameHeaderStatus.Invalid)
                    {
                        throw new InvalidDataException("invalid frame header");
                    }

                    if (filled - offset < size)
                    {
                        break;
                    }

                    var frame = buffer.AsMemory(offset, size);
                    offset += size;
                    await onPacket(frame);
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
        }
    }

    public bool Send(ReadOnlySpan<byte> packet)
    {
        if (FrameCodec.ReadHeader(packet, out var size, out _) != FrameHeaderStatus.Complete || size != packet.Length)
        {
            throw new ArgumentException("Expected one complete frame.", nameof(packet));
        }

        bool signal;
        lock (_sendGate)
        {
            if (_closed)
            {
                return false;
            }

            if (_pendingLength + packet.Length > _options.MaxPendingSendBytes)
            {
                _pendingLength = 0;
                CloseLocked("send backlog exceeded " + _options.MaxPendingSendBytes + " bytes");
                return false;
            }

            EnsureCapacity(_pendingLength + packet.Length);
            packet.CopyTo(_pending.AsSpan(_pendingLength));
            _pendingLength += packet.Length;
            signal = !_signalled;
            _signalled = true;
        }

        if (signal)
        {
            _sendSignal.Writer.TryWrite(true);
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
                if (!await _sendSignal.Reader.WaitToReadAsync() || IsClosed)
                {
                    return;
                }

                _sendSignal.Reader.TryRead(out _);

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

                await _stream.WriteAsync(writing.AsMemory(0, length), _lifetime.Token);
                await _stream.FlushAsync(_lifetime.Token);
                Interlocked.Add(ref _bytesSent, length);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            Close(ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(writing);
        }
    }

    // Lets already-queued packets (for example a login refusal) reach the stream before closing.
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
        _sendSignal.Writer.TryComplete();
        _lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        Close("disposed");
        try
        {
            await _stream.DisposeAsync();
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
        }

        _lifetime.Dispose();
        lock (_sendGate)
        {
            ArrayPool<byte>.Shared.Return(_pending);
            _pending = [];
        }
    }
}
