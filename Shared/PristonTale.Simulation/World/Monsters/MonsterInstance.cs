using System.Numerics;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.World.Monsters;

public sealed class MonsterInstance
{
    public MonsterInstance(uint entityId, MonsterDefinition definition, Vector3 home, float facing, int spawnPointIndex)
    {
        EntityId = entityId;
        Def = definition ?? throw new ArgumentNullException(nameof(definition));
        Position = home;
        HomeAnchor = home;
        Facing = facing;
        SpawnPointIndex = spawnPointIndex;
        Life = definition.Combat.MaxLife;
        Motion = MonsterMotionState.Walk;
        AiState = MonsterAiState.Wander;
        ProgressProbePosition = home;
        MoveTarget = home;
    }

    public uint EntityId { get; }

    public MonsterDefinition Def { get; }

    public Vector3 Position { get; set; }

    public float Facing { get; set; }

    public int Life { get; set; }

    public MonsterMotionState Motion { get; set; }

    public MonsterAiState AiState { get; set; }

    public Vector3 HomeAnchor { get; }

    public int SpawnPointIndex { get; }

    public Vector3 MoveTarget { get; set; }

    public uint TargetId { get; set; }

    public float SinceDecisionMs { get; set; }

    public float SinceRetargetMs { get; set; }

    public float TargetLockMsLeft { get; set; }

    public float AttackTimerMs { get; set; }

    public bool AttackHitFired { get; set; }

    public bool AttackIsSkill { get; set; }

    public uint SkillTargetId { get; set; }
    public float SkillFacing { get; set; }
    public Vector3 SkillOrigin { get; set; }
    public Vector3 SkillCenter { get; set; }
    public bool SkillImpactPending { get; set; }

    public bool SwingJustBegan { get; set; }

    public Vector3 ProgressProbePosition { get; set; }

    public float MovementCyclesSinceProbe { get; set; }

    public float SpawnWalkElapsedMs { get; set; }

    public float FlinchMsLeft { get; set; }

    public float IdleDespawnMsLeft { get; set; }

    public bool IsAlive => Life > 0 && AiState != MonsterAiState.Dead;

    public void ApplyDamage(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Life -= amount;
        if (Life <= 0)
        {
            Life = 0;
            SkillImpactPending = false;
            AiState = MonsterAiState.Dead;
            Motion = MonsterMotionState.Dead;
        }
    }
}
