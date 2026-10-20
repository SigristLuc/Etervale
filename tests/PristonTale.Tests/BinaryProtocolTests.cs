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
        yield return ("Priston packet cipher matches the default C++ English build", CipherAsync);
        yield return ("Priston server list preserves the C++ field layout and counts", ServerListAsync);
        yield return ("character info and PLAYERINFO keep the 32-bit x86 layout", CharacterInfoAsync);
        yield return ("character list is 1484 bytes with 240-byte character slots", CharacterListAsync);
        yield return ("ATTACK_DAMAGE uses the damage checksum and damage cipher keyed by the sender serial", AttackDamageAsync);
        yield return ("PLAYDATAGROUP nests self-sized play data and PLAYDATA entries", PlayDataGroupAsync);
        yield return ("RECORDDATA chunks follow the original chunk totals", RecordChunksAsync);
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
        Assert.True(written == expected.Length && encoded.SequenceEqual(expected), "encoded command differs from the C++ algorithm");
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

    private static Task CharacterInfoAsync()
    {
        var character = new CharacterInfo { Name = "Gear", ModelName = "char\\tmABCD\\a001.ini", JobCode = 2, Level = 7, LifeCurrent = 90, LifeMax = 120 };
        character.Temp[CharacterInfo.TempTemplateCode] = 0x1234;
        character.Experience = 0x1_0000_0002;
        var packet = new byte[PlayerInfoPacket.Size];
        var written = new PlayerInfoPacket(character, 0x4000_0001, 256, 512, -768).Write(packet);
        Assert.True(written == 504, "PLAYERINFO is not 504 bytes");
        Assert.True(Encoding.ASCII.GetString(packet, 8, 4) == "Gear" && packet[40] == (byte)'c', "character info names moved");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8 + 180)) == 2 && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8 + 184)) == 7,
            "class code or Level offset changed");
        Assert.True(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(8 + 292)) == 90 && BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(8 + 294)) == 120,
            "Life[2] offset changed");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8 + 328)) == 0, "lpMonInfo slot is not a zero 32-bit pointer");
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8 + 316)) == 2 && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8 + 388)) == 1,
            "Exp/Exp_High split changed");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8 + 408)) == 0x1234, "dwTemp offset changed");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(472)) == 0x4000_0001 && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(484)) == -768,
            "PLAYERINFO trailer moved");
        var decoded = PlayerInfoPacket.Read(packet);
        Assert.True(decoded.Character.Name == "Gear" && decoded.Character.Experience == 0x1_0000_0002 && decoded.Z == -768,
            "PLAYERINFO round trip changed fields");
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

    private static Task AttackDamageAsync()
    {
        var attack = new AttackDamagePacket(0x4000_0010, 0x4000_0020, 256, 0, -512, 1, 48 * 256, 10, 20, 0, 1234, 7, 0, 3);
        var packet = new byte[AttackDamagePacket.Size];
        Assert.True(attack.Write(packet) == 92, "ATTACK_DAMAGE is not 92 bytes");
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4)) == PristonOpcodes.AttackDamage
            && BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)) != 0x4000_0020, "damage cipher left the target serial readable");
        Assert.True(AttackDamagePacket.Read(packet, 0x4000_0010) == attack, "ATTACK_DAMAGE round trip changed fields");
        var rejected = false;
        try
        {
            AttackDamagePacket.Read(packet, 0x4000_0011);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.True(rejected, "a packet decoded with another serial passed the checksum");
        return Task.CompletedTask;
    }

    private static Task PlayDataGroupAsync()
    {
        var detailed = new TransPlayData(9, 4, 77, 30, 60, 1001, 3, -1, 2, 15,
            [new PlayBuffer(256, 0, -256, PristonUnits.FrameNone, 1024, MotionStateCode.Run, 4)]);
        var coarse = new PlayDataCoarse(10, 512, 0, 512, 2048, MotionStateCode.Walk, 1002);
        var buffer = new byte[PristonFrameCodec.MaxFrameSize];
        var group = new PlayDataGroupWriter(buffer, 5);
        var size = TransPlayData.SizeFor(1);
        Assert.True(size == 84, "one-buffer play data packet is not 84 bytes");
        group.Commit(detailed.Write(group.Reserve(size)));
        group.Commit(coarse.Write(group.Reserve(PlayDataCoarse.Size)));
        var packet = group.Finish().ToArray();
        Assert.True(packet.Length == 16 + 84 + 40 && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(8)) == 2, "PLAYDATAGROUP header changed");
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(16 + 16)) == 1 && BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(16 + 32)) == 3
            && BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(16 + 48)) == 256, "play data packet offsets changed");
        var entries = new List<byte[]>();
        PlayDataGroup.Read(packet, entry => entries.Add(entry.ToArray()));
        var read = TransPlayData.Read(entries[0]);
        Assert.True(read.ObjectSerial == 9 && read.Target == 4 && read.StartPosi == 77 && read.Hp == 30 && read.DescriptorRevision == 2 && read.Level == 15
            && read.Buffers.Single() == detailed.Buffers[0], "detailed entry changed");
        Assert.True(PlayDataCoarse.Read(entries[1]) == coarse, "coarse entry changed");
        return Task.CompletedTask;
    }

    private static Task RecordChunksAsync()
    {
        var record = new RecordDataPacket(new CharacterInfo { Name = "Record", ObjectSerial = 5 }, 3, 1000, -1000).Build();
        var chunks = RecordDataChunks.Split(record).ToArray();
        Assert.True(record.Length == 1776 && chunks.Length == 1 && chunks[0].Length == 1776 + 32
            && BinaryPrimitives.ReadInt32LittleEndian(chunks[0].AsSpan(12)) == 0, "single-chunk record must declare Total = 0");
        var assembled = new RecordDataAssembler().Add(chunks[0]);
        Assert.True(assembled is not null && RecordDataPacket.Read(assembled) is { FieldCode: 3, PosZ: -1000, Character.Name: "Record" },
            "record round trip failed");

        var large = new byte[20000];
        BinaryPrimitives.WriteInt32LittleEndian(large, large.Length);
        var parts = RecordDataChunks.Split(large).ToArray();
        Assert.True(parts.Length == 3 && parts.All(part => BinaryPrimitives.ReadInt32LittleEndian(part.AsSpan(12)) == 3), "multi-chunk Total changed");
        var assembler = new RecordDataAssembler();
        Assert.True(assembler.Add(parts[2]) is null && assembler.Add(parts[0]) is null, "record completed before every chunk arrived");
        Assert.True(assembler.Add(parts[1])?.SequenceEqual(large) == true, "out-of-order chunks were not reassembled");
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
            new TransCommand(PristonOpcodes.GetPlayerInfo, 0, index).Write(buffer);
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
