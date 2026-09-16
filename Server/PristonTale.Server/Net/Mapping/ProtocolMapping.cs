using System.Numerics;
using PristonTale.Protocol.Common;
using PristonTale.Protocol.Session;
using PristonTale.Protocol.World;
using PristonTale.Application.World;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Server.Net.Mapping;

public static class ProtocolMapping
{
    public static Vec3 ToProto(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };

    public static Vector3 ToNumeric(Vec3 v) => new(v.X, v.Y, v.Z);

    public static EntityKind ToProto(EntityKindView kind) => kind switch
    {
        EntityKindView.Player => EntityKind.Player,
        EntityKindView.Monster => EntityKind.Monster,
        EntityKindView.Npc => EntityKind.Npc,
        _ => EntityKind.Unspecified,
    };

    public static MoveState ToProto(MoveStateView state) => state switch
    {
        MoveStateView.Idle => MoveState.Idle,
        MoveStateView.Walking => MoveState.Walking,
        MoveStateView.Running => MoveState.Running,
        MoveStateView.Attack => MoveState.Attack,
        MoveStateView.Damage => MoveState.Damage,
        MoveStateView.Dead => MoveState.Dead,
        MoveStateView.Skill => MoveState.Skill,
        MoveStateView.Eat => MoveState.Eat,
        _ => MoveState.Unspecified,
    };

    public static EntitySpawn ToSpawnProto(EntityView e)
    {
        var spawn = new EntitySpawn
        {
            EntityId = e.EntityId,
            Kind = ToProto(e.Kind),
            IsSelf = e.IsSelf,
            Position = ToProto(e.Position),
            Facing = e.Facing,
            MoveState = ToProto(e.MoveState),
            Hp = e.Hp,
            MaxHp = e.MaxHp,
            MoveSpeed = e.MoveSpeed,
            AttackRate = e.AttackRate,
            AttackTargetId = e.AttackTargetId,
            AttackSequence = e.AttackSequence,
        };

        if (e.Kind == EntityKindView.Player)
            spawn.Player = ToAppearanceProto(e.Appearance, e.Level);
        else
            spawn.TemplateId = e.TemplateId;

        return spawn;
    }

    public static PlayerAppearance ToAppearanceProto(PlayerAppearanceView a, int level) => new()
    {
        Name = a.Name ?? string.Empty,
        JobCode = a.JobCode,
        Tier = a.Tier,
        FaceIndex = a.FaceIndex,
        HairIndex = a.HairIndex,
        ModelName = a.ModelName ?? string.Empty,
        ModelName2 = a.ModelName2 ?? string.Empty,
        Level = level,
        WeaponKey = a.WeaponKey ?? "",
        ArmorKey = a.ArmorKey ?? "",
        OffHandKey = a.OffHandKey ?? "",
        InVillage = a.InVillage,
    };

    public static EntityState ToStateProto(EntityView e) => new()
    {
        EntityId = e.EntityId,
        Position = ToProto(e.Position),
        Facing = e.Facing,
        MoveState = ToProto(e.MoveState),
        Hp = e.Hp,
        MoveSpeed = e.MoveSpeed,
        AttackRate = e.AttackRate,
        AttackTargetId = e.AttackTargetId,
        AttackSequence = e.AttackSequence,
    };

    public static CombatEvent ToProto(in CombatEventView e) => new()
    {
        AttackerId = e.AttackerId,
        TargetId = e.TargetId,
        Kind = e.Kind switch
        {
            CombatEventKindView.SwingBegin => CombatEventKind.SwingBegin,
            CombatEventKindView.Hit => CombatEventKind.Hit,
            CombatEventKindView.PotionBegin => CombatEventKind.PotionBegin,
            CombatEventKindView.PotionApplied => CombatEventKind.PotionApplied,
            CombatEventKindView.MonsterSkillImpact => CombatEventKind.MonsterSkillImpact,
            _ => CombatEventKind.Unspecified,
        },
        DurationMs = e.DurationMs,
        HitAtMs = e.HitAtMs,
        Damage = e.Damage,
        Critical = e.Crit,
        Killed = e.Killed,
        Resources = (uint)e.Resources,
        SourceSoundCode = e.SourceSoundCode,
        Position = e.Kind == CombatEventKindView.MonsterSkillImpact ? ToProto(e.Position) : null,
        Facing = e.Facing,
    };

    public static WorldCatalogReply ToCatalogProto(MonsterCatalog monsters, PristonTale.Simulation.World.Npcs.NpcCatalog npcs = null)
    {
        var reply = new WorldCatalogReply();
        foreach (var def in monsters.All)
        {
            reply.Monsters.Add(ToRenderProto(def));
        }

        if (npcs is not null)
        {
            foreach (var npc in npcs.All)
            {
                reply.Npcs.Add(new NpcRenderDef
                {
                    TemplateId = (uint)npc.Code,
                    ModelInxPath = npc.ModelInxPath,
                    ModelScale = npc.ModelScale,
                    Name = npc.Name,
                    Services = (uint)npc.Services,
                    SoundCode = npc.SoundCode
                });
            }
        }
        return reply;
    }

    public static MonsterRenderDef ToRenderProto(MonsterDefinition def) => new()
    {
        TemplateId = (uint)def.Code,
        ModelInxPath = def.ModelInxPath ?? string.Empty,
        ModelScale = def.ModelScale,
        Name = def.Name ?? string.Empty,
        Level = def.Combat.Level,
    };

    public static WorldUpdate ToProto(uint serverTick, PresenceDiff diff, int? fieldId = null)
    {
        var update = new WorldUpdate
        {
            ServerTick = serverTick
        };
        if (fieldId.HasValue)
        {
            update.FieldCode = fieldId.Value;
        }

        foreach (var spawn in diff.Spawns)
        {
            update.Spawns.Add(ToSpawnProto(spawn));
        }

        foreach (var state in diff.States)
        {
            update.States.Add(ToStateProto(state));
        }

        foreach (var id in diff.Removed)
        {
            update.Removed.Add(id);
        }

        foreach (var combatEvent in diff.Events)
        {
            update.CombatEvents.Add(ToProto(combatEvent));
        }

        foreach (var drop in diff.GroundAdded)
        {
            update.GroundAdded.Add(new GroundDrop
            {
                Id = drop.Id.ToString(),
                ItemCode = drop.ItemCode,
                CatalogKey = drop.CatalogKey,
                Name = drop.Name,
                Amount = drop.Amount,
                Gold = drop.Gold,
                Position = new Vec3 { X = drop.Position.X, Y = drop.Position.Y, Z = drop.Position.Z }
            });
        }

        foreach (var id in diff.GroundRemoved)
        {
            update.GroundRemoved.Add(id.ToString());
        }

        return update;
    }
}
