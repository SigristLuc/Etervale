using System.Collections.Generic;

namespace PristonTale.Application.World;

internal sealed class MonsterDamageLog
{
    private readonly Dictionary<uint, int> _byAttacker = new();

    public uint KillerId { get; private set; }

    public uint FirstAttackerId { get; private set; }

    public int FirstAttackerDamage { get; private set; }

    public long FirstAttackerLastHitMs { get; private set; }

    public void RecordHit(uint attackerId, int damage, bool monsterAtFullHpBeforeHit, long nowMs)
    {
        if (attackerId == 0 || damage <= 0)
            return;

        _byAttacker.TryGetValue(attackerId, out int prev);
        _byAttacker[attackerId] = prev + damage;
        KillerId = attackerId;

        if (FirstAttackerId == 0 && monsterAtFullHpBeforeHit)
            FirstAttackerId = attackerId;

        if (attackerId == FirstAttackerId)
        {
            FirstAttackerDamage += damage;
            FirstAttackerLastHitMs = nowMs;
        }
    }

    public int DamageBy(uint attackerId) => _byAttacker.TryGetValue(attackerId, out int d) ? d : 0;
}
