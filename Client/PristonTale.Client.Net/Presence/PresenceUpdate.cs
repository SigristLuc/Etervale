using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;
using PristonTale.Contracts.Social;

namespace PristonTale.Client.Net.Presence;

public sealed record PresenceUpdate(
    long ServerTick,
    IReadOnlyList<EntitySpawnInfo> Spawns,
    IReadOnlyList<EntityStateInfo> States,
    IReadOnlyList<ulong> Removed,
    IReadOnlyList<CombatEventInfo> Events,
    int? FieldId = null,
    OwnerState? Owner = null)
{
    public IReadOnlyList<PristonTale.Simulation.World.Loot.GroundDropView> GroundAdded { get; init; } = [];
    public IReadOnlyList<Guid> GroundRemoved { get; init; } = [];

    // FieldWarp for the local player: a forced correction after a rejected report, otherwise a field confirmation.
    public WarpInfo? Warp { get; init; }

    // Server simulation time of a freshly received group or event list (PresenceClock extension); null otherwise.
    public double? ServerTimeMs { get; init; }

    public IReadOnlyList<SkillEventInfo> SkillEvents { get; init; } = [];
    public IReadOnlyList<StatusEffectInfo> StatusEffects { get; init; } = [];
    public SkillCooldownSnapshot? Cooldowns { get; init; }
    public SkillCastResultInfo? CastResult { get; init; }

    public PremiumTimerSnapshot? PremiumTimers { get; init; }

    // What standing up again would cost the local player while it is dead.
    public PristonTale.Simulation.Progression.RestartOffer? RestartOffer { get; init; }
    public IReadOnlyList<ItemEffectInfo> ItemEffects { get; init; } = [];

    // The quest journal when it changed, quest notices and a quest dialog the world opened on its own.
    public QuestView? Quest { get; init; }
    public IReadOnlyList<QuestNotice> QuestNotices { get; init; } = [];
    public QuestPrompt? QuestPrompt { get; init; }

    // Chat lines from the server, and the server's request to save and quit (its shutdown).
    public IReadOnlyList<ChatLineInfo> Chat { get; init; } = [];
    public bool CloseRequested { get; init; }

    // The local player's party when it changed (no member once it has none), and a player's request to group.
    public PartyInfo? Party { get; init; }
    public PartyRequestInfo? PartyRequest { get; init; }
}

// The local player's party, the leader first; Position is the member's planar place (x, z) in world units.
public sealed record PartyInfo(IReadOnlyList<PartyMemberInfo> Members)
{
    public static readonly PartyInfo None = new([]);

    public bool Leads(ulong entityId) => Members.Count > 0 && Members[0].EntityId == entityId;

    public bool Contains(ulong entityId)
    {
        for (var index = 0; index < Members.Count; index++)
        {
            if (Members[index].EntityId == entityId)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record PartyMemberInfo(ulong EntityId, string Name, int Level, int Life, int MaxLife, System.Numerics.Vector2 Position);

// Recommended: the player a member asks its leader to take (empty for a request to join).
public sealed record PartyRequestInfo(ulong FromId, string FromName, string Recommended = "");

// One chat line: Kind is the server's colour and tab code, SpeakerId the entity that said it (0 for server lines).
// Direct lines (the plain chat message) go to the log as they are; the others show over a speaker the client sees.
public sealed record ChatLineInfo(ChatKind Kind, ulong SpeakerId, string Text);

public sealed record WarpInfo(int FieldId, System.Numerics.Vector3 Position, bool Forced);
