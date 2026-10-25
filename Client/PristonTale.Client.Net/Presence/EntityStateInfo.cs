using System.Numerics;

namespace PristonTale.Client.Net.Presence;

public sealed record EntityStateInfo(
    ulong EntityId,
    Vector3 Position,
    float Facing,
    RemoteMoveState MoveState,
    int Hp,
    int MoveSpeed = 0,
    float AttackRate = 1f,
    ulong AttackTargetId = 0,
    ulong AttackSequence = 0,
    double? ServerTimeMs = null);
