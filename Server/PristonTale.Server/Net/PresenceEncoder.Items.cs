using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;

namespace PristonTale.Server.Net;

// Item effects of the receiver and the players it sees, and the receiver's premium timers whenever they change.
internal sealed partial class PresenceEncoder
{
    private readonly List<ItemEffectEntry> _itemEffects = new();
    private long _premiumRevision = -1;

    private void SendItemEffects(in WorldSnapshot snapshot, IPacketSink connection)
    {
        _itemEffects.Clear();
        foreach (var effect in snapshot.ItemEffects)
        {
            if (Relevant(effect.EntityId, 0))
            {
                _itemEffects.Add(new ItemEffectEntry(effect.EntityId, effect.ItemCode, (byte)effect.Effect, PristonUnits.ToFixed(effect.Position.X),
                    PristonUnits.ToFixed(effect.Position.Y), PristonUnits.ToFixed(effect.Position.Z), effect.Variant));
            }
        }

        if (_itemEffects.Count == 0)
        {
            return;
        }

        SendClock(connection);
        for (var offset = 0; offset < _itemEffects.Count; offset += ItemEffectsPacket.MaxEffects)
        {
            var batch = _itemEffects.GetRange(offset, Math.Min(ItemEffectsPacket.MaxEffects, _itemEffects.Count - offset));
            connection.Send(_scratch.AsSpan(0, new ItemEffectsPacket(batch).Write(_scratch)));
        }
    }

    private void SendPremium(OwnerState? owner, IPacketSink connection)
    {
        if (owner is null || owner.PremiumRevision == _premiumRevision)
        {
            return;
        }

        _premiumRevision = owner.PremiumRevision;
        var timers = owner.PremiumTimers.Take(PremiumTimersPacket.MaxTimers)
            .Select(timer => new PremiumTimerEntry((byte)timer.Kind, timer.Value, (uint)Math.Clamp(timer.ExpiresAtSeconds - owner.PremiumClockSeconds, 0, uint.MaxValue),
                (uint)Math.Clamp(timer.DurationSeconds, 0, uint.MaxValue)))
            .ToArray();
        connection.Send(_scratch.AsSpan(0, new PremiumTimersPacket(owner.PremiumRevision, timers).Write(_scratch)));
    }
}
