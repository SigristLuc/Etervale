using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

internal interface IFieldCombatHost
{
    bool TryGetPlayer(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player);
    bool TryGetMonster(uint entityId, [MaybeNullWhen(false)] out MonsterInstance monster);
    IEnumerable<MonsterInstance> Monsters { get; }
    IReadOnlyList<PreyCandidate> NearbyPlayers(Vector3 origin, float radius);
    void DespawnMonster(uint entityId);

    MonsterAiTuning TuningFor(MonsterDefinition def);

    (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer player);

    void EmitCombatEvent(in CombatEventView combatEvent);
}

public readonly record struct KillPayout(uint MonsterEntityId, uint CreditedPlayerId, int ExpAwarded, IReadOnlyList<RolledDrop> Drops);

internal sealed class FieldCombat
{
    public const float CorpseDespawnMs = 5710f;

    private readonly IFieldCombatHost _host;
    private readonly CombatRng _rng;
    private readonly int _experienceMultiplier;

    private readonly Dictionary<uint, MonsterDamageLog> _damageLogs = new();
    private readonly Dictionary<uint, float> _corpseMsLeft = new();
    private readonly Dictionary<uint, long> _expByPlayer = new();

    private readonly List<uint> _expiredScratch = new();

    public FieldCombat(IFieldCombatHost host, CombatRng rng, int experienceMultiplier = 1)
    {
        _host = host;
        _rng = rng;
        _experienceMultiplier = experienceMultiplier;
    }

    public long ExpFor(uint playerId) => _expByPlayer.TryGetValue(playerId, out long e) ? e : 0;

    // Skill effects that join normal attacks on this field (enchants, shields, reflection, evasion).
    internal IPlayerSkillCombat? SkillCombat { get; set; }

    public void StepPlayerAttack(WorldPlayer player)
    {
        uint targetId = player.AttackTargetId;
        if (targetId == 0 || player.IsDead)
        {
            return;
        }

        if (!_host.TryGetMonster(targetId, out var monster) || !monster.IsAlive || monster.Effects.Has(ControlFlags.Charmed) || monster.IsSummon)
        {
            player.ClearAttackTarget();
            return;
        }

        if (player.IsBusy)
        {
            return;
        }

        if (!player.InAttackRange(monster.Position))
        {
            // The client walks into range and reports it, as in the original; the lock waits for that position.
            return;
        }

        (float durationMs, float hitAtMs) = _host.PlayerSwingTiming(player);
        player.BeginSwing(targetId, monster.Position, durationMs, hitAtMs);
        _host.EmitCombatEvent(new CombatEventView(
            player.EntityId, targetId, CombatEventKindView.SwingBegin, durationMs, hitAtMs));
    }

    public KillPayout? ResolvePlayerSwingHit(WorldPlayer player, uint targetId, long nowMs)
    {
        if (player.IsDead)
            return null;

        if (!_host.TryGetMonster(targetId, out var monster) || !monster.IsAlive)
            return null;

        AttackProfile atk = SkillFormulas.NormalAttack(player.Character);
        atk = atk with { ExtraPercent = player.Character.Effects.Totals.Flat(StatKind.NextHitDamage) };
        DefenderProfile def = DefenderProfile.FromMonster(monster);
        HitResult result = CombatResolver.Resolve(_rng, atk, def, defenderHasShield: false);

        if (result.Damage <= 0)
            return null;

        SkillCombat?.ConsumeNextHit(player);

        var damage = ModifierRules.ScaleDamage(result.Damage, player.Character.Effects.Totals, monster.Effects.Totals);
        damage = SkillCombat?.BasicStrike(player, monster, damage) ?? damage;
        return ApplyPlayerDamage(player, monster, damage, result.Crit, nowMs);
    }

    // Shared by swings and skills: credits the hit, emits it and pays out a kill.
    // A summon's or familiar's blow names its own entity as the attacker and still credits the owner.
    // A player's own hit on a wild monster (not a familiar's or a summon's), for the effects that answer each attack.
    public Action<WorldPlayer, int>? PlayerHitLanded { get; set; }

    public KillPayout? ApplyPlayerDamage(WorldPlayer player, MonsterInstance monster, int damage, bool crit, long nowMs, bool forceFlinch = false,
        uint sourceEntityId = 0)
    {
        if (damage <= 0 || !monster.IsAlive)
        {
            return null;
        }

        bool monsterAtFullHp = monster.Life >= monster.Def.Combat.MaxLife;
        Log(monster.EntityId).RecordHit(player.EntityId, damage, monsterAtFullHp, nowMs);
        if (sourceEntityId == 0 && !monster.IsSummon)
        {
            PlayerHitLanded?.Invoke(player, damage);
        }

        monster.ApplyDamage(damage);
        SkillCombat?.MonsterDamaged(monster);

        MonsterAiBrain.ApplyBossRetargetOnHit(monster, sourceEntityId != 0 ? sourceEntityId : player.EntityId);

        _host.EmitCombatEvent(new CombatEventView(
            sourceEntityId != 0 ? sourceEntityId : player.EntityId, monster.EntityId, CombatEventKindView.Hit,
            damage: damage, crit: crit, killed: !monster.IsAlive));

        if (!monster.IsAlive)
            return OnMonsterDeath(monster, nowMs);

        MonsterAiBrain.ApplyFlinchOnHit(
            monster, _rng, damage, crit || forceFlinch, _host.TuningFor(monster.Def).FlinchDurationMs);

        return null;
    }

    public void ResolveMonsterHit(MonsterInstance attacker, in MonsterAttackEvent landed)
    {
        if (!attacker.IsAlive || landed.AttackerEntityId != attacker.EntityId)
        {
            return;
        }

        if (landed.IsSkill)
        {
            ResolveMonsterSkill(attacker, landed.TargetEntityId);
            return;
        }

        if (_host.TryGetMonster(landed.TargetEntityId, out var summon) && summon.IsSummon)
        {
            ResolveMonsterVersusSummon(attacker, summon, skill: false);
            return;
        }

        if (!_host.TryGetPlayer(landed.TargetEntityId, out var target) || target.IsDead
            || !MonsterAttackReach.CanLandBasic(attacker.Def, attacker.Position, target.Position))
        {
            return;
        }

        SkillCombat?.Reflect(attacker, target);
        if (!attacker.IsAlive || SkillCombat?.Evades(attacker, target) == true)
        {
            return;
        }

        AttackProfile atk = CombatProfileFactory.ForMonsterBasicAttack(attacker.Def.Combat);
        DefenderProfile def = CombatProfileFactory.ForPlayerDefender(target.Character);
        HitResult result = CombatResolver.ResolveMonsterAttack(_rng, atk, def, CombatProfileFactory.HasShield(target.Character));
        if (result.Blocked)
        {
            SkillCombat?.Blocked(attacker, target);
        }

        if (result.Damage > 0)
        {
            // Debuffs on the monster (damage dealt) and the player's buffs (damage taken, undead absorption) apply to
            // its normal attacks, as the original applies them before the hit reaches the player.
            var damage = ModifierRules.ScaleDamage(result.Damage, attacker.Effects.Totals, target.Character.Effects.Totals);
            damage = ModifierRules.AgainstUndead(damage, attacker.Def.Brood == MonsterBrood.Undead, target.Character.Effects.Totals);
            damage = SkillCombat?.Defend(attacker, target, damage, false) ?? damage;
            target.ApplyDamage(damage);
            _host.EmitCombatEvent(new CombatEventView(
                attacker.EntityId, landed.TargetEntityId, CombatEventKindView.Hit,
                damage: damage, crit: result.Crit, killed: target.IsDead));
        }
    }

    // A wild monster's blow on a summon: its accuracy against the summon's defence, its damage (or skill damage) less the
    // summon's absorption percent.
    private void ResolveMonsterVersusSummon(MonsterInstance attacker, MonsterInstance summon, bool skill)
    {
        if (!summon.IsAlive)
        {
            return;
        }

        var combat = attacker.Def.Combat;
        var attack = new AttackProfile(combat.AttackRatingAccuracy, combat.AttackDamage.Min, combat.AttackDamage.Max, combat.Level, Element.Bionic, 0, 0, 0);
        if (!CombatResolver.RollHit(_rng, CombatResolver.HitChance(attack, DefenderProfile.FromMonster(summon))))
        {
            return;
        }

        var range = skill ? attacker.Def.Skill.SkillDamage : combat.AttackDamage;
        var damage = CombatResolver.ApplyAbsorbMonster(_rng.Range(range.Min, Math.Max(range.Min, range.Max)), summon.Def.Combat.Absorption);
        summon.ApplyDamage(damage);
        _host.EmitCombatEvent(new CombatEventView(attacker.EntityId, summon.EntityId, CombatEventKindView.Hit, damage: damage, killed: !summon.IsAlive));
        if (!summon.IsAlive)
        {
            BeginCorpse(summon.EntityId);
        }
    }

    // A summon that died outside a player's kill (expiry, a monster's blow) leaves a corpse like any monster.
    public void BeginCorpse(uint monsterId) => _corpseMsLeft.TryAdd(monsterId, CorpseDespawnMs);

    private void ResolveMonsterSkill(MonsterInstance attacker, uint targetId)
    {
        if (!attacker.AttackIsSkill || !attacker.SkillImpactPending || targetId != attacker.SkillTargetId
            || !_host.TuningFor(attacker.Def).CanCastOffensiveSkill)
        {
            return;
        }

        // Consume once before visiting victims. Retried/stale events cannot apply another area hit.
        attacker.SkillImpactPending = false;
        _host.EmitCombatEvent(new CombatEventView(
            attacker.EntityId, targetId, CombatEventKindView.MonsterSkillImpact,
            sourceSoundCode: attacker.Def.SoundCode, position: attacker.SkillOrigin, facing: attacker.SkillFacing));

        if (attacker.Def.Skill.SkillRange == 0)
        {
            if (_host.TryGetMonster(targetId, out var summon) && summon.IsSummon)
            {
                ResolveMonsterVersusSummon(attacker, summon, skill: true);
                return;
            }

            ApplyMonsterSkillToPlayer(attacker, targetId);
            return;
        }

        var candidates = _host.NearbyPlayers(attacker.SkillOrigin, MonsterOffensiveSkill.QueryRadius(attacker.Def.Skill));
        for (int index = 0; index < candidates.Count; index++)
        {
            ApplyMonsterSkillToPlayer(attacker, candidates[index].EntityId);
        }
    }

    private void ApplyMonsterSkillToPlayer(MonsterInstance attacker, uint targetId)
    {
        if (!_host.TryGetPlayer(targetId, out var target) || target.IsDead || target.Hp <= 0
            || !MonsterOffensiveSkill.Contains(attacker.Def.Skill, attacker.SkillOrigin, attacker.SkillCenter, target.Position))
        {
            return;
        }

        var defender = CombatProfileFactory.ForPlayerDefender(target.Character);
        var result = CombatResolver.ResolveMonsterSkill(_rng, attacker.Def.Skill.SkillDamage, defender);
        var damage = ModifierRules.AgainstUndead(result.Damage, attacker.Def.Brood == MonsterBrood.Undead, target.Character.Effects.Totals);
        damage = SkillCombat?.Defend(attacker, target, damage, true) ?? damage;
        target.ApplyDamage(damage);
        _host.EmitCombatEvent(new CombatEventView(
            attacker.EntityId, targetId, CombatEventKindView.Hit, damage: damage, killed: target.IsDead));
    }

    private KillPayout OnMonsterDeath(MonsterInstance monster, long nowMs)
    {
        MonsterDamageLog log = Log(monster.EntityId);

        // Kill credit: killer unless a latched first-attacker dealt ≥40% MaxLife and stayed engaged (15 s).
        uint credited = log.KillerId;
        bool firstIsKiller = log.FirstAttackerId == log.KillerId;
        bool stillEngaged = (nowMs - log.FirstAttackerLastHitMs) <= KillReward.FirstAttackerDisengageMs;
        if (KillReward.FirstAttackerStealsCredit(
                firstIsKiller, log.FirstAttackerDamage, monster.Def.Combat.MaxLife, stillEngaged))
        {
            credited = log.FirstAttackerId;
        }

        int exp = 0;
        if (credited != 0 && _host.TryGetPlayer(credited, out var creditedPlayer))
        {
            exp = KillReward.ExpForKill(monster.Def.ExpReward, monster.Def.Combat.Level, creditedPlayer.Level, _experienceMultiplier);
            exp = (int)Math.Min(int.MaxValue, PremiumRules.BoostExperience(exp, creditedPlayer.Character.Premium));
            _expByPlayer.TryGetValue(credited, out long total);
            _expByPlayer[credited] = total + exp;
            creditedPlayer.Character.AwardExperience(exp);
        }

        var dropBonus = credited != 0 && _host.TryGetPlayer(credited, out var looter) ? PremiumRules.ExtraDropChance(looter.Character.Premium) : 0;
        IReadOnlyList<RolledDrop> drops = DropRoller.Roll(_rng, monster.Def.Drops, extraRollChance: dropBonus);

        _damageLogs.Remove(monster.EntityId);
        _corpseMsLeft[monster.EntityId] = CorpseDespawnMs;

        return new KillPayout(monster.EntityId, credited, exp, drops);
    }

    public void TickCorpses(float dtMs)
    {
        if (_corpseMsLeft.Count == 0)
            return;

        _expiredScratch.Clear();
        foreach (var kv in _corpseMsLeft)
        {
            float left = kv.Value - dtMs;
            if (left <= 0f)
                _expiredScratch.Add(kv.Key);
            else
                _corpseMsLeft[kv.Key] = left;
        }

        for (int i = 0; i < _expiredScratch.Count; i++)
        {
            uint id = _expiredScratch[i];
            _corpseMsLeft.Remove(id);
            _host.DespawnMonster(id);
        }
    }

    public void ForgetMonster(uint entityId)
    {
        _damageLogs.Remove(entityId);
        _corpseMsLeft.Remove(entityId);
    }

    private MonsterDamageLog Log(uint monsterId)
    {
        if (!_damageLogs.TryGetValue(monsterId, out var log))
        {
            log = new MonsterDamageLog();
            _damageLogs[monsterId] = log;
        }
        return log;
    }
}
