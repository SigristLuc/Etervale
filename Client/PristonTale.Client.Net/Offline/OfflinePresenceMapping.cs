using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Client.Net.Presence;

namespace PristonTale.Client.Net.Offline;

internal static class OfflinePresenceMapping
{
    public static PresenceUpdate ToNeutral(uint serverTick, PresenceDiff diff, int? fieldId = null, OwnerState? owner = null)
    {
        var spawns = new List<EntitySpawnInfo>(diff.Spawns.Count);
        foreach (var s in diff.Spawns)
        {
            spawns.Add(ToSpawn(s));
        }

        var states = new List<EntityStateInfo>(diff.States.Count);
        foreach (var st in diff.States)
        {
            states.Add(ToState(st));
        }

        var events = new List<CombatEventInfo>(diff.Events.Count);
        foreach (var e in diff.Events)
        {
            events.Add(ToEvent(e));
        }

        return new PresenceUpdate(
            serverTick,
            spawns,
            states,
            new List<uint>(diff.Removed),
            events,
            fieldId,
            owner)
        { GroundAdded = diff.GroundAdded, GroundRemoved = diff.GroundRemoved };
    }

    private static EntitySpawnInfo ToSpawn(EntityView e)
    {
        PlayerAppearanceInfo? player = null;
        uint templateId = 0;
        if (e.Kind == EntityKindView.Player)
            player = ToAppearance(e.Appearance, e.Level);
        else
            templateId = e.TemplateId;

        return new EntitySpawnInfo(
            e.EntityId,
            ToKind(e.Kind),
            e.IsSelf,
            e.Position,
            e.Facing,
            ToMoveState(e.MoveState),
            e.Hp,
            e.MaxHp,
            player,
            templateId,
            e.MoveSpeed,
            e.AttackRate,
            e.AttackTargetId,
            e.AttackSequence);
    }

    private static EntityStateInfo ToState(EntityView e) =>
        new(
            e.EntityId,
            e.Position,
            e.Facing,
            ToMoveState(e.MoveState),
            e.Hp,
            e.MoveSpeed,
            e.AttackRate,
            e.AttackTargetId,
            e.AttackSequence);

    private static CombatEventInfo ToEvent(in CombatEventView e) =>
        new(
            e.AttackerId,
            e.TargetId,
            e.Kind switch
            {
                CombatEventKindView.SwingBegin => RemoteCombatEventKind.SwingBegin,
                CombatEventKindView.Hit => RemoteCombatEventKind.Hit,
                CombatEventKindView.PotionBegin => RemoteCombatEventKind.PotionBegin,
                CombatEventKindView.PotionApplied => RemoteCombatEventKind.PotionApplied,
                CombatEventKindView.MonsterSkillImpact => RemoteCombatEventKind.MonsterSkillImpact,
                _ => RemoteCombatEventKind.Unspecified,
            },
            e.DurationMs,
            e.HitAtMs,
            e.Damage,
            e.Crit,
            e.Killed,
            (int)e.Resources,
            e.SourceSoundCode,
            e.Position,
            e.Facing);

    private static PlayerAppearanceInfo ToAppearance(PlayerAppearanceView a, int level) =>
        new(
            a.Name ?? string.Empty,
            a.JobCode,
            a.Tier,
            a.FaceIndex,
            a.HairIndex,
            a.ModelName ?? string.Empty,
            a.ModelName2 ?? string.Empty,
            level, a.WeaponKey, a.ArmorKey, a.OffHandKey, a.InVillage);

    private static RemoteEntityKind ToKind(EntityKindView kind) => kind switch
    {
        EntityKindView.Player => RemoteEntityKind.Player,
        EntityKindView.Monster => RemoteEntityKind.Monster,
        EntityKindView.Npc => RemoteEntityKind.Npc,
        _ => RemoteEntityKind.Unspecified,
    };

    private static RemoteMoveState ToMoveState(MoveStateView state) => state switch
    {
        MoveStateView.Idle => RemoteMoveState.Idle,
        MoveStateView.Walking => RemoteMoveState.Walking,
        MoveStateView.Running => RemoteMoveState.Running,
        MoveStateView.Attack => RemoteMoveState.Attack,
        MoveStateView.Damage => RemoteMoveState.Damage,
        MoveStateView.Dead => RemoteMoveState.Dead,
        MoveStateView.Skill => RemoteMoveState.Skill,
        MoveStateView.Eat => RemoteMoveState.Eat,
        _ => RemoteMoveState.Unspecified,
    };
}
