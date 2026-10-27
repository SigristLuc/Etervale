using System.Collections.Generic;

namespace PristonTale.Application.World;

internal sealed class MonsterDamageLog
{
    private readonly Dictionary<ulong, int> _byAttacker = new();

    public ulong KillerId { get; private set; }

    public ulong FirstAttackerId { get; private set; }

    public int FirstAttackerDamage { get; private set; }

    public long FirstAttackerLastHitMs { get; private set; }

    public void RecordHit(ulong attackerId, int damage, bool monsterAtFullLifeBeforeHit, long nowMs)
    {
        if (attackerId == 0 || damage <= 0)
            return;

        _byAttacker.TryGetValue(attackerId, out int prev);
        _byAttacker[attackerId] = prev + damage;
        KillerId = attackerId;

        if (FirstAttackerId == 0 && monsterAtFullLifeBeforeHit)
            FirstAttackerId = attackerId;

        if (attackerId == FirstAttackerId)
        {
            FirstAttackerDamage += damage;
            FirstAttackerLastHitMs = nowMs;
        }
    }

    public int DamageBy(ulong attackerId) => _byAttacker.TryGetValue(attackerId, out int d) ? d : 0;
}
