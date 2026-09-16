using System.Numerics;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Common;
using PristonTale.Protocol.Session;

namespace PristonTale.Client.Net.Online;

internal static class PresenceMapping
{
    public static PresenceUpdate ToNeutral(WorldUpdate update)
    {
        var spawns = new List<EntitySpawnInfo>(update.Spawns.Count);
        foreach (var s in update.Spawns)
        {
            spawns.Add(ToNeutral(s));
        }

        var states = new List<EntityStateInfo>(update.States.Count);
        foreach (var st in update.States)
        {
            states.Add(ToNeutral(st));
        }

        var events = new List<CombatEventInfo>(update.CombatEvents.Count);
        foreach (var e in update.CombatEvents)
        {
            events.Add(ToNeutral(e));
        }

        return new PresenceUpdate(
            update.ServerTick,
            spawns,
            states,
            new List<uint>(update.Removed),
            events,
            update.HasFieldCode ? update.FieldCode : null,
            OwnerStateMapping.ToNeutral(update.Owner))
        {
            GroundAdded = update.GroundAdded.Select(drop => new PristonTale.Simulation.World.Loot.GroundDropView(
                Guid.Parse(drop.Id), drop.ItemCode, drop.CatalogKey, drop.Name, drop.Amount, drop.Gold,
                new Vector3(drop.Position.X, drop.Position.Y, drop.Position.Z))).ToArray(),
            GroundRemoved = update.GroundRemoved.Select(Guid.Parse).ToArray()
        };
    }

    public static EntitySpawnInfo ToNeutral(EntitySpawn spawn)
    {
        // Model the appearance oneof: a player carries the full appearance (template id 0); a
        // monster/NPC carries a catalog template id (appearance null). Neither set ⇒ both defaults.
        PlayerAppearanceInfo? player = null;
        uint templateId = 0;
        switch (spawn.AppearanceCase)
        {
            case EntitySpawn.AppearanceOneofCase.Player:
                player = ToNeutral(spawn.Player);
                break;
            case EntitySpawn.AppearanceOneofCase.TemplateId:
                templateId = spawn.TemplateId;
                break;
            case EntitySpawn.AppearanceOneofCase.None:
            default:
                break;
        }

        return new EntitySpawnInfo(
            spawn.EntityId,
            ToNeutral(spawn.Kind),
            spawn.IsSelf,
            ToVector3(spawn.Position),
            spawn.Facing,
            ToNeutral(spawn.MoveState),
            spawn.Hp,
            spawn.MaxHp,
            player,
            templateId,
            spawn.MoveSpeed,
            spawn.AttackRate,
            spawn.AttackTargetId,
            spawn.AttackSequence);
    }

    public static EntityStateInfo ToNeutral(EntityState state) =>
        new(
            state.EntityId,
            ToVector3(state.Position),
            state.Facing,
            ToNeutral(state.MoveState),
            state.Hp,
            state.MoveSpeed,
            state.AttackRate,
            state.AttackTargetId,
            state.AttackSequence);

    public static CombatEventInfo ToNeutral(CombatEvent e) =>
        new(
            e.AttackerId,
            e.TargetId,
            e.Kind switch
            {
                CombatEventKind.SwingBegin => RemoteCombatEventKind.SwingBegin,
                CombatEventKind.Hit => RemoteCombatEventKind.Hit,
                CombatEventKind.PotionBegin => RemoteCombatEventKind.PotionBegin,
                CombatEventKind.PotionApplied => RemoteCombatEventKind.PotionApplied,
                CombatEventKind.MonsterSkillImpact => RemoteCombatEventKind.MonsterSkillImpact,
                _ => RemoteCombatEventKind.Unspecified,
            },
            e.DurationMs,
            e.HitAtMs,
            e.Damage,
            e.Critical,
            e.Killed,
            (int)e.Resources,
            e.SourceSoundCode,
            ToVector3(e.Position),
            e.Facing);

    public static PlayerAppearanceInfo ToNeutral(PlayerAppearance a) =>
        new(
            a.Name,
            a.JobCode,
            a.Tier,
            a.FaceIndex,
            a.HairIndex,
            a.ModelName,
            a.ModelName2,
            a.Level, a.WeaponKey, a.ArmorKey, a.OffHandKey, a.InVillage);

    private static Vector3 ToVector3(Vec3 v) =>
        v is null ? Vector3.Zero : new Vector3(v.X, v.Y, v.Z);

    private static RemoteEntityKind ToNeutral(EntityKind kind) => kind switch
    {
        EntityKind.Player => RemoteEntityKind.Player,
        EntityKind.Monster => RemoteEntityKind.Monster,
        EntityKind.Npc => RemoteEntityKind.Npc,
        _ => RemoteEntityKind.Unspecified,
    };

    private static RemoteMoveState ToNeutral(MoveState state) => state switch
    {
        MoveState.Idle => RemoteMoveState.Idle,
        MoveState.Walking => RemoteMoveState.Walking,
        MoveState.Running => RemoteMoveState.Running,
        MoveState.Attack => RemoteMoveState.Attack,
        MoveState.Damage => RemoteMoveState.Damage,
        MoveState.Dead => RemoteMoveState.Dead,
        MoveState.Skill => RemoteMoveState.Skill,
        MoveState.Eat => RemoteMoveState.Eat,
        _ => RemoteMoveState.Unspecified,
    };
}
