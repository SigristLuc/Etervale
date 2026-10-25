using System.Buffers.Binary;
using System.Text;
using System.Net;
using System.Net.Sockets;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;

namespace PristonTale.Tests;

internal static class BinaryProtocolTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("Priston TCP header handles partial reads and rejects invalid lengths", HeaderAsync);
        yield return ("Priston packet cipher matches the default English client build", CipherAsync);
        yield return ("hello, login, character list and commands and heartbeats round-trip with UTF-8 text", AccountAsync);
        yield return ("movement reports, attack requests and field warps round-trip with 64-bit targets and sequences", PlayerActionsAsync);
        yield return ("world entry, join and leave messages round-trip with 64-bit ids and UTF-8 names", WorldEntryAsync);
        yield return ("replay window rejects repeats and stale counters and survives the 16-bit wrap", ReplayWindowAsync);
        yield return ("TCP connection delivers encrypted and plain packets and coalesces sends", ConnectionAsync);
    }

    private static Task HeaderAsync()
    {
        Span<byte> packet = stackalloc byte[PristonFrameCodec.HeaderSize];
        Assert.True(PristonFrameCodec.ReadHeader(packet[..7], out _, out _) == PristonHeaderStatus.NeedMoreData, "partial TCP header was accepted");
        Assert.True(PristonFrameCodec.WriteFrame(packet, 0x48470080, []) == 8, "empty command frame has the wrong length");
        Assert.True(PristonFrameCodec.ReadHeader(packet, out var size, out var code) == PristonHeaderStatus.Complete
            && size == 8 && code == 0x48470080, "Priston little-endian frame header changed");
        BinaryPrimitives.WriteInt32LittleEndian(packet, 8193);
        Assert.True(PristonFrameCodec.ReadHeader(packet, out _, out _) == PristonHeaderStatus.Invalid, "oversized frame was accepted");
        BinaryPrimitives.WriteInt32LittleEndian(packet, 7);
        Assert.True(PristonFrameCodec.ReadHeader(packet, out _, out _) == PristonHeaderStatus.Invalid, "undersized frame was accepted");
        return Task.CompletedTask;
    }

    private static Task CipherAsync()
    {
        byte[] plain =
        [
            0x18, 0x00, 0x00, 0x00, 0x80, 0x00, 0x47, 0x48,
            0x44, 0x33, 0x22, 0x11, 0x88, 0x77, 0x66, 0x55,
            0x04, 0x03, 0x02, 0x01, 0x0D, 0x0C, 0x0B, 0x0A
        ];
        byte[] expected =
        [
            0x20, 0x00, 0x00, 0x00, 0xAB, 0x03, 0x01, 0x80,
            0x10, 0x00, 0x34, 0x12, 0xDD, 0x11, 0x90, 0x08,
            0xF3, 0x5F, 0x06, 0x32, 0xA8, 0x75, 0x6B, 0x24,
            0xAD, 0xA3, 0x24, 0xC2, 0x97, 0x0B, 0xA2, 0x04
        ];
        Span<byte> encoded = stackalloc byte[32];
        var written = PristonPacketCipher.Encode(plain, encoded, 0x1234, 0x0010, 0xED);
        Assert.True(written == expected.Length && encoded.SequenceEqual(expected), "encoded command differs from the reference cipher output");
        Span<byte> decoded = stackalloc byte[24];
        Assert.True(PristonPacketCipher.TryDecode(expected, decoded, 0xED, out var sequence, out var length)
            && sequence == 0x0010 && length == plain.Length && decoded.SequenceEqual(plain), "Priston command did not decode");

        expected[15] ^= 1;
        Assert.True(!PristonPacketCipher.TryDecode(expected, decoded, 0xED, out _, out _), "corrupt checksum was accepted");
        expected[15] ^= 1;
        Assert.True(!PristonPacketCipher.TryDecode(expected[..^1], decoded, 0xED, out _, out _), "truncated encrypted packet was accepted");
        return Task.CompletedTask;
    }

    private static Task AccountAsync()
    {
        var buffer = new byte[PristonFrameCodec.MaxFrameSize];
        var hello = new ServerHelloPacket(ProtocolVersion.Current, ProtocolVersion.Minimum, 12);
        Assert.True(ServerHelloPacket.Read(buffer.AsSpan(0, hello.Write(buffer))) == hello, "server hello round trip changed fields");
        var login = new LoginPacket(ProtocolVersion.Current, "conta", "sênha✓");
        Assert.True(LoginPacket.Read(buffer.AsSpan(0, login.Write(buffer))) == login, "login round trip changed the account or password");
        Assert.True(LoginResultPacket.Read(buffer.AsSpan(0, new LoginResultPacket(LoginStatus.Busy).Write(buffer))).Status == LoginStatus.Busy,
            "login result lost its status");

        var entry = new CharacterListEntry("Álvaro", 6, 42, 1, 2, 1, 0x01010300, 0x02010100, 0, true);
        var list = new CharacterListPacket([entry, entry with { Name = "Beta", InVillage = false }]);
        var read = CharacterListPacket.Read(buffer.AsSpan(0, list.Write(buffer)));
        Assert.True(read.Characters.SequenceEqual(list.Characters), "character list round trip changed fields");
        Assert.True(CharacterListPacket.Read(buffer.AsSpan(0, new CharacterListPacket([]).Write(buffer))).Characters.Count == 0, "an empty list did not read back");

        var create = new CharacterCommandPacket(CharacterAction.Create, 3, 2, "Nova");
        Assert.True(CharacterCommandPacket.Read(buffer.AsSpan(0, create.Write(buffer))) == create, "character command round trip changed fields");
        var length = create.Write(buffer);
        buffer[10] = 9;
        Assert.ThrowsAsync(() => Task.Run(() => CharacterCommandPacket.Read(buffer.AsSpan(0, length).ToArray()))).Wait();
        var result = new CharacterResultPacket(CharacterAction.Create, false, 4, "Nova");
        Assert.True(CharacterResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "character result round trip changed fields");
        var beat = new HeartbeatPacket(uint.MaxValue, long.MaxValue);
        Assert.True(HeartbeatPacket.Read(buffer.AsSpan(0, beat.Write(buffer))) == beat, "heartbeat round trip changed fields");
        return Task.CompletedTask;
    }

    private static Task PlayerActionsAsync()
    {
        var buffer = new byte[PristonFrameCodec.MaxFrameSize];
        var report = new MovementReportPacket(PresenceMotion.Run, 3, new System.Numerics.Vector3(1024.5f, -12.25f, -999_999f), MathF.PI / 2,
            0x1_0000_0002UL);
        Assert.True(report.Write(buffer) == MovementReportPacket.Size, "movement report size changed");
        var read = MovementReportPacket.Read(buffer.AsSpan(0, MovementReportPacket.Size));
        Assert.True(read with { Facing = report.Facing } == report && MathF.Abs(read.Facing - report.Facing) < 1e-4f && read.Moving,
            $"movement report read back as {read}");
        buffer[10] = PresenceMotion.Attack;
        Assert.ThrowsAsync(() => Task.Run(() => MovementReportPacket.Read(buffer.AsSpan(0, MovementReportPacket.Size).ToArray()))).Wait();

        var attack = new AttackRequestPacket(0x1_0000_0003UL, true, ulong.MaxValue - 1);
        Assert.True(AttackRequestPacket.Read(buffer.AsSpan(0, attack.Write(buffer))) == attack, "attack request round trip changed fields");
        buffer[10] = 2;
        Assert.ThrowsAsync(() => Task.Run(() => AttackRequestPacket.Read(buffer.AsSpan(0, AttackRequestPacket.Size).ToArray()))).Wait();

        var warp = new FieldWarpPacket(21, 512.5f, -256.25f, true);
        Assert.True(FieldWarpPacket.Read(buffer.AsSpan(0, warp.Write(buffer))) == warp, "field warp round trip changed fields");
        return Task.CompletedTask;
    }

    private static Task WorldEntryAsync()
    {
        var buffer = new byte[PristonFrameCodec.MaxFrameSize];
        var enter = new EnterWorldPacket("Hélio");
        Assert.True(EnterWorldPacket.Read(buffer.AsSpan(0, enter.Write(buffer))) == enter, "world entry request round trip changed the name");
        var entry = new WorldEntryPacket(0, 0x1_0000_0005UL, 3, 1000.5f, -1000.25f);
        var read = WorldEntryPacket.Read(buffer.AsSpan(0, entry.Write(buffer)));
        Assert.True(read == entry && read.Entered, "world entry round trip changed fields");
        Assert.True(!WorldEntryPacket.Read(buffer.AsSpan(0, new WorldEntryPacket(5, 0, 0, 0, 0).Write(buffer))).Entered, "a refused entry read as entered");
        Assert.True(JoinWorldPacket.Read(buffer.AsSpan(0, new JoinWorldPacket(ulong.MaxValue).Write(buffer))).EntityId == ulong.MaxValue,
            "world join lost its 64-bit id");
        var joined = new WorldJoinedPacket(7, long.MaxValue);
        Assert.True(WorldJoinedPacket.Read(buffer.AsSpan(0, joined.Write(buffer))) == joined, "world joined round trip changed fields");
        Assert.True(LeaveWorldPacket.Read(buffer.AsSpan(0, new LeaveWorldPacket(true).Write(buffer))).Saved, "leave answer lost its saved flag");
        return Task.CompletedTask;
    }

    private static Task ReplayWindowAsync()
    {
        var window = new PristonReplayWindow();
        Assert.True(window.TryAccept(0x10) && window.TryAccept(0x12) && !window.TryAccept(0x10), "repeated counter accepted");
        Assert.True(window.TryAccept(0x11), "reordered recent counter rejected");
        for (ushort counter = 0x13; counter < 0x40; counter++)
        {
            window.TryAccept(counter);
        }

        Assert.True(!window.TryAccept(0x20), "counter 16 behind the highest was accepted");
        var wrapping = new PristonReplayWindow();
        for (var counter = 0xFFF0; counter <= 0x1_0005; counter++)
        {
            Assert.True(wrapping.TryAccept((ushort)counter), "counter wrap stopped the stream");
        }

        return Task.CompletedTask;
    }

    private static async Task ConnectionAsync()
    {
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var client = new Socket(SocketType.Stream, ProtocolType.Tcp);
        var accept = listener.AcceptAsync();
        await client.ConnectAsync(listener.LocalEndPoint!);
        await using var server = new PristonConnection(await accept);
        await using var sender = new PristonConnection(client);
        var received = new List<byte[]>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverRun = server.RunAsync(packet =>
        {
            received.Add(packet.ToArray());
            if (received.Count == 101)
            {
                done.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }, timeout.Token);
        var senderRun = sender.RunAsync(_ => ValueTask.CompletedTask, timeout.Token);
        var buffer = new byte[HeartbeatPacket.Size];
        var beat = new HeartbeatPacket(1, 0x0102_0304_0506);
        beat.Write(buffer);
        Assert.True(sender.SendEncrypted(buffer), "encrypted send was refused");
        for (var index = 0; index < 100; index++)
        {
            new HeartbeatPacket((uint)index, 0).Write(buffer);
            sender.Send(buffer);
        }

        await done.Task.WaitAsync(timeout.Token);
        Assert.True(HeartbeatPacket.Read(received[0]) == beat, "encrypted packet changed in transit");
        Assert.True(received.Skip(1).Select(packet => (int)HeartbeatPacket.Read(packet).Counter).SequenceEqual(Enumerable.Range(0, 100)), "plain packets reordered");
        Assert.True(sender.BytesSent == 24 + 8 + 100 * 24, "encrypted envelope size or coalesced byte count changed");
        sender.Close("test done");
        await Task.WhenAll(serverRun, senderRun);
    }
}
