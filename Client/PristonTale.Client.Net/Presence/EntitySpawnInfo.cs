using System.Numerics;

namespace PristonTale.Client.Net.Presence;

public sealed record EntitySpawnInfo(
    uint EntityId,
    RemoteEntityKind Kind,
    bool IsSelf,
    Vector3 Position,
    float Facing,
    RemoteMoveState MoveState,
    int Hp,
    int MaxHp,
    PlayerAppearanceInfo? Player,
    uint TemplateId,
    int MoveSpeed = 0,
    float AttackRate = 1f,
    uint AttackTargetId = 0,
    ulong AttackSequence = 0,
    double? ServerTimeMs = null,
    string Name = "",
    int Level = 0);
