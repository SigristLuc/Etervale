using PristonTale.Simulation.World.Loot;
using System.Numerics;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

public enum EntityKindView { Player, Monster, Npc }

public enum MoveStateView { Idle, Walking, Running, Attack, Damage, Dead, Skill, Eat }

public readonly struct PlayerAppearanceView
{
    public PlayerAppearanceView(
        string name, int jobCode, int tier, int faceIndex, int hairIndex,
        string modelName, string modelName2)
    {
        Name = name;
        JobCode = jobCode;
        Tier = tier;
        FaceIndex = faceIndex;
        HairIndex = hairIndex;
        ModelName = modelName;
        ModelName2 = modelName2;
    }

    public string WeaponKey { get; init; } = "";
    public string ArmorKey { get; init; } = "";
    public string OffHandKey { get; init; } = "";
    public bool InVillage { get; init; }

    // Premium looks: the helper pet (1 Terry, 2 Nepsys, 3 Io, 4 Mute), the Phoenix and the big head (1 or 2).
    public int HelperPet { get; init; }
    public bool Phoenix { get; init; }
    public int BigHead { get; init; }

    public string Name { get; }
    public int JobCode { get; }
    public int Tier { get; }
    public int FaceIndex { get; }
    public int HairIndex { get; }
    public string ModelName { get; }
    public string ModelName2 { get; }
}

public readonly struct EntityView
{
    public EntityView(
        uint entityId, EntityKindView kind, bool isSelf, Vector3 position, float facing,
        MoveStateView moveState, int level, int hp, int maxHp,
        PlayerAppearanceView appearance, uint templateId = 0,
        int moveSpeed = 0, float attackRate = 1f, uint attackTargetId = 0, ulong attackSequence = 0)
    {
        EntityId = entityId;
        Kind = kind;
        IsSelf = isSelf;
        Position = position;
        Facing = facing;
        MoveState = moveState;
        Level = level;
        Hp = hp;
        MaxHp = maxHp;
        Appearance = appearance;
        TemplateId = templateId;
        MoveSpeed = moveSpeed;
        AttackRate = attackRate;
        AttackTargetId = attackTargetId;
        AttackSequence = attackSequence;
    }

    public uint EntityId { get; }
    public EntityKindView Kind { get; }

    // A summon's owner and its own model size (0 keeps the catalog size).
    public uint OwnerId { get; init; }
    public float ModelScale { get; init; }

    public bool IsSelf { get; }

    public Vector3 Position { get; }
    public float Facing { get; }
    public MoveStateView MoveState { get; }
    public int Level { get; }

    public int Hp { get; }
    public int MaxHp { get; }

    public PlayerAppearanceView Appearance { get; }
    public uint TemplateId { get; }

    public int MoveSpeed { get; }

    public float AttackRate { get; }
    public uint AttackTargetId { get; }
    public ulong AttackSequence { get; }
}

public enum CombatEventKindView
{
    SwingBegin = 1,

    Hit = 2,
    PotionBegin = 3,
    PotionApplied = 4,
    MonsterSkillImpact = 5,
    // A skill or periodic effect restored life; Damage carries the amount.
    Heal = 6,
    // The target evaded a monster's attack through a skill effect.
    Evade = 7,
    // A dead player rose again (a revive skill); Damage carries the life it returns with.
    Revive = 8,
}

public enum SkillEventPhaseView : byte
{
    Start = 1,
    Impact = 2,
    Cancel = 3,
    // A held charge was released: the motion jumps ahead; DurationMs and HitAtMs give the new remaining timeline.
    Release = 4
}

[Flags]
public enum SkillEventFlagsView : ushort
{
    None = 0,
    Area = 1,
    Projectile = 2,
    SelfCast = 4,
    // A chain link, a strike of the caster's familiar, a buff given to another player, a counter strike of a shield
    // buff and a fully charged resolution.
    Chain = 8,
    Companion = 16,
    Ally = 32,
    Counter = 64,
    Charged = 128,
    // Two bits: the resolution mode picked for the cast (Mode1 = 256, Mode2 = 512).
    Mode1 = 256,
    Mode2 = 512
}

public readonly record struct SkillEventView(
    uint CasterId,
    uint TargetId,
    uint SkillCode,
    SkillEventPhaseView Phase,
    int Rank,
    SkillEventFlagsView Flags,
    float DurationMs,
    float HitAtMs,
    Vector3 Position,
    float Facing,
    int TargetCount,
    float Radius);

public enum StatusEventOpView : byte
{
    Add = 1,
    Refresh = 2,
    Remove = 3,
    Tick = 4
}

public readonly record struct StatusEventView(
    uint EntityId,
    uint SourceId,
    uint StatusCode,
    StatusEventOpView Op,
    int Stacks,
    byte SourceKind,
    byte Control,
    bool Harmful,
    bool Permanent,
    int DurationMs,
    int RemainingMs,
    int Value);

public readonly record struct SkillCooldownView(uint SkillCode, int RemainingMs, int DurationMs);

public readonly struct CombatEventView
{
    public CombatEventView(
        uint attackerId, uint targetId, CombatEventKindView kind,
        float durationMs = 0f, float hitAtMs = 0f, int damage = 0, bool crit = false, bool killed = false, int resources = 0,
        int sourceSoundCode = 0, Vector3 position = default, float facing = 0f)
    {
        AttackerId = attackerId;
        TargetId = targetId;
        Kind = kind;
        DurationMs = durationMs;
        HitAtMs = hitAtMs;
        Damage = damage;
        Crit = crit;
        Killed = killed;
        Resources = resources;
        SourceSoundCode = sourceSoundCode;
        Position = position;
        Facing = facing;
    }

    public uint AttackerId { get; }
    public uint TargetId { get; }
    public CombatEventKindView Kind { get; }
    public float DurationMs { get; }
    public float HitAtMs { get; }
    public int Damage { get; }
    public bool Crit { get; }
    public bool Killed { get; }
    public int Resources { get; }
    public int SourceSoundCode { get; }
    public Vector3 Position { get; }
    public float Facing { get; }
}

public readonly struct WorldSnapshot
{
    public WorldSnapshot(uint serverTick, EntityView[] entities, CombatEventView[]? events = null, int? fieldId = null, OwnerState? owner = null, GroundDropState[]? groundDrops = null)
    {
        ServerTick = serverTick;
        Entities = entities;
        Events = events ?? Array.Empty<CombatEventView>();
        FieldId = fieldId;
        Owner = owner;
        GroundDrops = groundDrops ?? [];
    }

    public uint ServerTick { get; }
    public EntityView[] Entities { get; }
    public CombatEventView[] Events { get; }
    public int? FieldId { get; }
    public OwnerState? Owner { get; }
    public GroundDropState[] GroundDrops { get; }

    // Built once per field and tick; subscribers share it to find nearby entities without scanning the field.
    public EntityGrid? Grid { get; init; }

    public SkillEventView[] SkillEvents { get; init; } = [];
    public StatusEventView[] StatusEvents { get; init; } = [];
    public ItemEffectView[] ItemEffects { get; init; } = [];

    // The receiver's running cooldowns; null when they did not change since the last snapshot it was sent.
    public SkillCooldownView[]? Cooldowns { get; init; }

    public WorldSnapshot WithOwner(OwnerState? owner, SkillCooldownView[]? cooldowns = null) => new(ServerTick, Entities, Events, FieldId, owner, GroundDrops)
    {
        Grid = Grid,
        SkillEvents = SkillEvents,
        StatusEvents = StatusEvents,
        ItemEffects = ItemEffects,
        Cooldowns = cooldowns
    };

    public WorldSnapshot WithEvents(CombatEventView[] events) => new(ServerTick, Entities, events, FieldId, Owner, GroundDrops)
    {
        Grid = Grid,
        SkillEvents = SkillEvents,
        StatusEvents = StatusEvents,
        ItemEffects = ItemEffects,
        Cooldowns = Cooldowns
    };
}
