using System.Numerics;
using PristonTale.Contracts.Motion;

namespace PristonTale.Client.Net.Presence;

public sealed record EntitySpawnInfo(
    ulong EntityId,
    RemoteEntityKind Kind,
    bool IsSelf,
    Vector3 Position,
    float Facing,
    MotionState MoveState,
    int Hp,
    int MaxHp,
    PlayerAppearanceInfo? Player,
    uint TemplateId,
    int MoveSpeed = 0,
    float AttackRate = 1f,
    ulong AttackTargetId = 0,
    ulong AttackSequence = 0,
    double? ServerTimeMs = null,
    string Name = "",
    int Level = 0)
{
    // A summon: its owner's serial and its own model scale (0 keeps the catalog's).
    public ulong OwnerId { get; init; }
    public float ModelScale { get; init; }
}
