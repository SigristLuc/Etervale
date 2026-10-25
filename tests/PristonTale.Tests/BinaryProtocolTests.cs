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
        yield return ("Priston server list preserves the classic field layout and counts", ServerListAsync);
        yield return ("character list is 1484 bytes with 240-byte character slots", CharacterListAsync);
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

    private static Task ServerListAsync()
    {
        var entry = new PristonServerEntry("Alpha", "127.0.0.1", "", "", 10012, 0, 0, 1);
        var source = new PristonServerList("Etervale", 0x12345678, 7, 0, 1, [entry]);
        Span<byte> packet = stackalloc byte[PristonServerListCodec.HeaderSize + PristonServerListCodec.EntrySize];
        var written = PristonServerListCodec.Write(packet, source, Encoding.ASCII);
        Assert.True(written == 152, "single-entry server list has the wrong Win32 size");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]) == PristonServerListCodec.PacketCode, "server-list opcode changed");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet[24..]) == 0x12345678, "server time has the wrong offset");
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(packet[36..]) == 0
            && BinaryPrimitives.ReadInt32LittleEndian(packet[40..]) == 1, "server counts have the wrong offsets");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet[136..]) == 10012
            && BinaryPrimitives.ReadUInt32LittleEndian(packet[148..]) == 1, "entry ports or connection state have the wrong offsets");
        Assert.True(PristonServerListCodec.TryRead(packet, Encoding.ASCII, out var decoded)
            && decoded is not null
            && decoded.Name == source.Name
            && decoded.GameServerCount == 1
            && decoded.Entries[0] == entry, "server-list round trip changed fields");
        var rejectedLossyName = false;
        try
        {
            PristonServerListCodec.Write(new byte[packet.Length], source with { Name = "Étervale" }, Encoding.ASCII);
        }
        catch (ArgumentException)
        {
            rejectedLossyName = true;
        }

        Assert.True(rejectedLossyName, "a lossy server name was written to the fixed char field");
        BinaryPrimitives.WriteInt32LittleEndian(packet[36..], int.MaxValue);
        Assert.True(!PristonServerListCodec.TryRead(packet, Encoding.ASCII, out _), "overflowing server counts were accepted");
        return Task.CompletedTask;
    }

    private static Task CharacterListAsync()
    {
        var entry = new UserCharacterEntry("Alpha", "", "", 6, 42, 0, 0x02010100, 3, 1024, -2048, 2, 1, 1, 0x01010300, 0, true);
        var packet = new byte[UserCharacterList.Size];
        Assert.True(new UserCharacterList("acct", [entry]).Write(packet) == 1484, "character list is not 1484 bytes");
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(40)) == 1, "PlayUserCount offset changed");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(44 + 160)) == 6 && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(44 + 164)) == 42
            && BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(44 + 172)) == 0x02010100
            && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(44 + 184)) == -2048, "character slot field offsets changed");
        Assert.True(UserCharacterList.Read(packet).Characters.Single() == entry, "character list round trip changed fields");
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
        var buffer = new byte[TransCommand.Size];
        new TransCommand(PristonOpcodes.CheckNetState, 1, 2, 3, 4).Write(buffer);
        Assert.True(sender.SendEncrypted(buffer), "encrypted send was refused");
        for (var index = 0; index < 100; index++)
        {
            new TransCommand(EtervaleOpcodes.DescribeEntity, 0, index).Write(buffer);
            sender.Send(buffer);
        }

        await done.Task.WaitAsync(timeout.Token);
        Assert.True(TransCommand.Read(received[0]) == new TransCommand(PristonOpcodes.CheckNetState, 1, 2, 3, 4), "encrypted packet changed in transit");
        Assert.True(received.Skip(1).Select(packet => TransCommand.Read(packet).WParam).SequenceEqual(Enumerable.Range(0, 100)), "plain packets reordered");
        Assert.True(sender.BytesSent == 24 + 8 + 100 * 24, "encrypted envelope size or coalesced byte count changed");
        sender.Close("test done");
        await Task.WhenAll(serverRun, senderRun);
    }
}
