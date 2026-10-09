using PristonTale.Application.World;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Net;

namespace PristonTale.Tests;

// Runs snapshots through the server's PresenceEncoder and the client's PresenceDecoder over original packet bytes,
// answering GETPLAYERINFO the way ClientSession does.
internal sealed class WireRoundTrip
{
    private readonly PresenceEncoder _encoder;
    private readonly PresenceDecoder _decoder;
    private readonly CapturingSink _sink = new();
    private readonly Queue<uint> _requests = new();

    public WireRoundTrip(uint self, WorldDataHolder? data = null)
    {
        _encoder = new PresenceEncoder(self, data ?? new WorldDataHolder());
        Catalog = new WorldRenderCatalog();
        _decoder = new PresenceDecoder(Catalog, _requests.Enqueue) { SelfSerial = self };
    }

    public WorldRenderCatalog Catalog { get; }

    public int BytesSent => _sink.Bytes;

    public List<PresenceUpdate> Push(in WorldSnapshot snapshot)
    {
        _encoder.Encode(snapshot, _sink);
        var updates = new List<PresenceUpdate>();
        while (_sink.Packets.Count > 0 || _requests.Count > 0)
        {
            while (_sink.Packets.TryDequeue(out var packet))
            {
                if (_decoder.TryHandle(packet, out var update) && update is not null)
                {
                    updates.Add(update);
                }
            }

            while (_requests.TryDequeue(out var serial))
            {
                if (_encoder.Describe(serial) is { } info)
                {
                    var buffer = new byte[PlayerInfoPacket.Size];
                    info.Write(buffer);
                    _sink.Packets.Enqueue(buffer);
                }
            }
        }

        return updates;
    }

    // Merges one snapshot's updates into the single view the scene would observe.
    public PresenceUpdate PushMerged(in WorldSnapshot snapshot)
    {
        var updates = Push(snapshot);
        return new PresenceUpdate(
            snapshot.ServerTick,
            updates.SelectMany(update => update.Spawns).ToList(),
            updates.SelectMany(update => update.States).ToList(),
            updates.SelectMany(update => update.Removed).ToList(),
            updates.SelectMany(update => update.Events).ToList(),
            updates.Select(update => update.FieldId).LastOrDefault(field => field.HasValue),
            updates.Select(update => update.Owner).LastOrDefault(owner => owner is not null))
        {
            GroundAdded = updates.SelectMany(update => update.GroundAdded).ToList(),
            GroundRemoved = updates.SelectMany(update => update.GroundRemoved).ToList(),
            Warp = updates.Select(update => update.Warp).LastOrDefault(warp => warp is not null)
        };
    }

    private sealed class CapturingSink : IPacketSink
    {
        public Queue<byte[]> Packets { get; } = new();

        public int Bytes { get; private set; }

        public bool Send(ReadOnlySpan<byte> packet)
        {
            Assert.True(packet.Length <= PristonFrameCodec.MaxFrameSize, "presence packet exceeds the 8192-byte frame");
            Packets.Enqueue(packet.ToArray());
            Bytes += packet.Length;
            return true;
        }

        public bool SendEncrypted(ReadOnlySpan<byte> packet) => Send(packet);
    }
}
