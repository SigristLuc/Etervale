using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.World.Ai;

public sealed class MonsterAiBrain
{
    private readonly ISimRng _rng;

    public MonsterAiBrain(ISimRng rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    public MonsterAttackEvent? Tick(MonsterInstance monster, MonsterAiTuning tuning, IPreyView prey, float dtMs)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(prey);
        if (!float.IsFinite(dtMs) || dtMs <= 0f)
        {
            return null;
        }

        if (monster.AiState == MonsterAiState.Dead || !monster.IsAlive)
        {
            return null;
        }

        monster.SinceDecisionMs += dtMs;
        monster.SinceRetargetMs += dtMs;
        if (monster.TargetLockMsLeft > 0f)
        {
            monster.TargetLockMsLeft = MathF.Max(0f, monster.TargetLockMsLeft - dtMs);
        }

        // Damage motion suppresses decisions and movement until its clip drains.
        if (monster.Motion == MonsterMotionState.Damage)
        {
            monster.FlinchMsLeft -= dtMs;
            if (monster.FlinchMsLeft <= 0f)
            {
                bool targetLive = monster.TargetId != 0 && prey.TryGet(monster.TargetId, out PreyCandidate tgt) && tgt.IsAlive;
                if (targetLive)
                {
                    monster.AiState = MonsterAiState.Chase;
                    monster.Motion = MonsterMotionState.Walk;
                }
                else
                {
                    EnterIdle(monster);
                }
            }

            return null;
        }

        if (monster.AiState == MonsterAiState.Attack)
        {
            return AdvanceAttack(monster, tuning, prey, dtMs);
        }

        CheckMovementProgress(monster, tuning, dtMs);

        if (monster.SinceRetargetMs >= MonsterAiTuning.RetargetIntervalMs
            && monster.TargetLockMsLeft <= 0f
            && monster.AiState != MonsterAiState.Leash)
        {
            monster.SinceRetargetMs = 0f;
            monster.TargetId = AcquireNearestPrey(monster, tuning, prey);
        }

        if (monster.SinceDecisionMs >= MonsterAiTuning.DecisionIntervalMs)
        {
            monster.SinceDecisionMs %= MonsterAiTuning.DecisionIntervalMs;
            return Decide(monster, tuning, prey, dtMs);
        }

        if (monster.Motion.IsMoving())
        {
            if (monster.AiState == MonsterAiState.Wander)
            {
                StepWander(monster, tuning, dtMs);
            }
            else
            {
                IntegrateToward(monster, monster.MoveTarget, monster.Motion == MonsterMotionState.Run, dtMs);
            }
        }

        return null;
    }

    private MonsterAttackEvent? Decide(MonsterInstance m, MonsterAiTuning t, IPreyView prey, float decisionDt)
    {
        // Resolve aggro before stuck handling so a fresh target survives its spawn tick.
        PreyCandidate live = default;
        bool hasTarget = m.TargetId != 0 && prey.TryGet(m.TargetId, out live) && live.IsAlive;
        if (!hasTarget)
        {
            live = default;
            m.TargetId = 0;
        }

        float homeDistSq = PlanarDistanceSq(m.Position, m.HomeAnchor);
        if (homeDistSq > t.LeashSq)
        {
            m.TargetId = 0;
            hasTarget = false;
            m.AiState = MonsterAiState.Leash;
            m.MoveTarget = m.HomeAnchor;
        }

        MonsterAttackEvent? result;
        switch (m.AiState)
        {
            case MonsterAiState.Leash:
                StepLeash(m, t, decisionDt);
                result = null;
                break;

            case MonsterAiState.Idle:
            case MonsterAiState.Wander:
                if (hasTarget)
                {
                    EnterChase(m);
                    result = StepChase(m, t, live, decisionDt);
                }
                else
                {
                    StepWander(m, t, decisionDt);
                    result = null;
                }

                break;

            case MonsterAiState.Chase:
                if (!hasTarget)
                {
                    EnterIdle(m);
                    result = null;
                }
                else
                {
                    result = StepChase(m, t, live, decisionDt);
                }

                break;

            default:
                result = null;
                break;
        }

        return result;
    }

    private static uint AcquireNearestPrey(MonsterInstance m, MonsterAiTuning t, IPreyView prey)
    {
        IReadOnlyList<PreyCandidate> candidates = prey.Nearby(m.Position, MathF.Sqrt(t.SightSq) + 1f);
        uint best = 0;
        long bestSq = long.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            PreyCandidate c = candidates[i];
            if (!c.IsAlive)
            {
                continue;
            }

            if (MathF.Abs(c.Position.Y - m.Position.Y) >= t.VerticalAggroGateF)
            {
                continue;
            }

            long dx = (long)(c.Position.X - m.Position.X);
            long dz = (long)(c.Position.Z - m.Position.Z);
            long dy = (long)(c.Position.Y - m.Position.Y);
            long distSq = (dx * dx) + (dy * dy) + (dz * dz);
            if (distSq < bestSq && distSq < t.SightSq)
            {
                bestSq = distSq;
                best = c.EntityId;
            }
        }

        return best;
    }

    private static void EnterChase(MonsterInstance m)
    {
        m.AiState = MonsterAiState.Chase;
        m.Motion = MonsterMotionState.Walk;
    }

    private static void EnterIdle(MonsterInstance m)
    {
        m.AiState = MonsterAiState.Idle;
        m.Motion = MonsterMotionState.Stand;
        m.TargetId = 0;
    }

    private MonsterAttackEvent? StepChase(MonsterInstance m, MonsterAiTuning t, PreyCandidate target, float dt)
    {
        long distSq = (long)PlanarDistanceSq(m.Position, target.Position);

        if (MonsterAttackReach.CanBegin(m.Def, m.Position, target.Position))
        {
            FaceToward(m, target.Position);
            BeginAttack(m, t);
            return null;
        }

        bool run = t.CanRun && distSq < t.RunDistanceSq;
        m.Motion = run ? MonsterMotionState.Run : MonsterMotionState.Walk;
        m.MoveTarget = target.Position;
        IntegrateToward(m, target.Position, run, dt);
        return null;
    }

    private static void StepWander(MonsterInstance m, MonsterAiTuning t, float dt)
    {
        if (m.AiState == MonsterAiState.Idle)
        {
            return;
        }

        // OpenMonster seeds TargetMoveCount=2. Without prey, walk in the spawn direction for two clips, then stand.
        float remainingMs = 2f * t.WalkCycleMs - m.SpawnWalkElapsedMs;
        if (remainingMs <= 0f)
        {
            EnterIdle(m);
            return;
        }

        float elapsed = MathF.Min(dt, remainingMs);
        var forward = new Vector3(MathF.Sin(m.Facing), 0f, MathF.Cos(m.Facing));
        m.MoveTarget = m.Position + forward * 10000f;
        IntegrateToward(m, m.MoveTarget, run: false, elapsed);
        m.SpawnWalkElapsedMs += elapsed;
        if (m.SpawnWalkElapsedMs >= 2f * t.WalkCycleMs)
        {
            EnterIdle(m);
        }
    }

    private static void CheckMovementProgress(MonsterInstance m, MonsterAiTuning t, float dtMs)
    {
        if (!m.Motion.IsMoving())
        {
            m.MovementCyclesSinceProbe = 0f;
            m.ProgressProbePosition = m.Position;
            return;
        }

        float cycleMs = m.Motion == MonsterMotionState.Run ? t.RunCycleMs : t.WalkCycleMs;
        m.MovementCyclesSinceProbe += dtMs / cycleMs;
        if (m.MovementCyclesSinceProbe < 5f)
        {
            return;
        }

        bool stuck = PlanarDistanceSq(m.Position, m.ProgressProbePosition) < t.StuckDistanceSq;
        m.ProgressProbePosition = m.Position;
        m.MovementCyclesSinceProbe = 0f;
        if (stuck && m.AiState == MonsterAiState.Chase)
        {
            m.TargetId = 0;
            m.AiState = MonsterAiState.Leash;
            m.Motion = MonsterMotionState.Walk;
            m.MoveTarget = m.HomeAnchor;
        }
    }

    private static void StepLeash(MonsterInstance m, MonsterAiTuning t, float dt)
    {
        m.Motion = MonsterMotionState.Walk;
        m.MoveTarget = m.HomeAnchor;
        IntegrateToward(m, m.HomeAnchor, run: false, dt);

        if (PlanarDistanceSq(m.Position, m.HomeAnchor) < t.StuckDistanceSq)
        {
            EnterIdle(m);
        }
    }

    private void BeginAttack(MonsterInstance m, MonsterAiTuning tuning)
    {
        m.AiState = MonsterAiState.Attack;
        m.Motion = MonsterMotionState.Attack;
        m.AttackTimerMs = 0f;
        m.MovementCyclesSinceProbe = 0f;
        m.ProgressProbePosition = m.Position;
        m.AttackHitFired = false;
        m.SwingJustBegan = true;

        // Skill roll on attack entry: rand()%100 < SkillRating (character.cpp:5731-5741).
        int rating = m.Def.Skill.SkillRatingPercent;
        m.AttackIsSkill = tuning.CanCastOffensiveSkill && _rng.NextExclusive(0, 100) < rating;
        m.SkillImpactPending = false;
        if (m.AttackIsSkill)
        {
            m.Motion = MonsterMotionState.Skill;
            m.SkillTargetId = m.TargetId;
            m.SkillOrigin = m.Position;
            m.SkillFacing = m.Facing;
            m.SkillCenter = MonsterOffensiveSkill.AreaCenter(m.Position, m.Facing, m.Def.Skill);
        }
    }

    private static MonsterAttackEvent? AdvanceAttack(MonsterInstance m, MonsterAiTuning t, IPreyView prey, float dtMs)
    {
        m.AttackTimerMs += dtMs;

        MonsterAttackEvent? landed = null;

        float hitAtMs = m.AttackIsSkill ? t.SkillWindupMs : t.WindupMs;
        float durationMs = m.AttackIsSkill ? t.SkillDurationMs : t.CooldownMs;

        // Area casts own their placement even if the original target dies; the host filters live victims at impact.
        if (!m.AttackHitFired && m.AttackTimerMs >= hitAtMs)
        {
            m.AttackHitFired = true;
            if (m.AttackIsSkill)
            {
                m.SkillImpactPending = true;
                landed = new MonsterAttackEvent(m.EntityId, m.SkillTargetId, true);
            }
            else if (m.TargetId != 0 && prey.TryGet(m.TargetId, out PreyCandidate tgt) && tgt.IsAlive
                && MonsterAttackReach.CanLandBasic(m.Def, m.Position, tgt.Position))
            {
                landed = new MonsterAttackEvent(m.EntityId, m.TargetId, m.AttackIsSkill);
            }
        }

        if (m.AttackTimerMs >= durationMs)
        {
            bool stillValid = m.TargetId != 0 && prey.TryGet(m.TargetId, out PreyCandidate tgt2) && tgt2.IsAlive;
            if (stillValid)
            {
                m.AiState = MonsterAiState.Chase;
                m.Motion = MonsterMotionState.Walk;
            }
            else
            {
                EnterIdle(m);
            }
        }

        return landed;
    }

    public static void ApplyFlinchOnHit(MonsterInstance monster, ISimRng rng, int damage, bool crit, float durationMs)
    {
        ArgumentNullException.ThrowIfNull(monster);
        ArgumentNullException.ThrowIfNull(rng);

        if (!monster.IsAlive || damage <= 0)
        {
            return;
        }

        if (monster.Motion is MonsterMotionState.Damage or MonsterMotionState.Dead)
        {
            return;
        }

        int maxLife = monster.Def.Combat.MaxLife;
        int multiplier = monster.Def.Combat.Level > 14 ? 5 : 10;
        bool significant = maxLife > 0 && (long)damage * multiplier >= maxLife;
        if (!significant && !crit)
        {
            return;
        }

        if (rng.NextExclusive(0, 100) >= monster.Def.Combat.DamageStunPercent)
        {
            return;
        }

        if (monster.AiState == MonsterAiState.Attack)
        {
            // Interrupt the swing: its windup blow never lands (mark it fired so AdvanceAttack can't).
            monster.AttackHitFired = true;
            monster.AiState = MonsterAiState.Chase;
        }

        monster.SkillImpactPending = false;
        monster.MovementCyclesSinceProbe = 0f;
        monster.ProgressProbePosition = monster.Position;
        monster.Motion = MonsterMotionState.Damage;
        monster.FlinchMsLeft = durationMs;
    }

    public static void ApplyBossRetargetOnHit(MonsterInstance boss, uint attackerEntityId)
    {
        ArgumentNullException.ThrowIfNull(boss);
        if (!boss.Def.IsBoss)
        {
            return;
        }

        if (boss.TargetLockMsLeft > 0f)
        {
            return;
        }

        boss.TargetId = attackerEntityId;
        boss.TargetLockMsLeft = MonsterAiTuning.BossTargetLockMs;
        if (boss.AiState is MonsterAiState.Idle or MonsterAiState.Wander or MonsterAiState.Leash)
        {
            boss.AiState = MonsterAiState.Chase;
            boss.Motion = MonsterMotionState.Walk;
        }
    }

    private static void IntegrateToward(MonsterInstance m, Vector3 target, bool run, float dtMs)
    {
        float dx = target.X - m.Position.X;
        float dz = target.Z - m.Position.Z;
        float distSq = (dx * dx) + (dz * dz);
        if (distSq < 1e-4f)
        {
            return;
        }

        float unitsPerSec = MonsterLocomotion.UnitsPerSecond(m.Def, run);

        float dist = MathF.Sqrt(distSq);
        float step = unitsPerSec * (dtMs / 1000f);
        m.Facing = MathF.Atan2(dx, dz);

        if (step >= dist)
        {
            m.Position = new Vector3(target.X, m.Position.Y, target.Z);
            return;
        }

        float nx = m.Position.X + (dx / dist * step);
        float nz = m.Position.Z + (dz / dist * step);
        m.Position = new Vector3(nx, m.Position.Y, nz);
    }

    private static void FaceToward(MonsterInstance m, Vector3 target)
        => m.Facing = MathF.Atan2(target.X - m.Position.X, target.Z - m.Position.Z);

    private static float PlanarDistanceSq(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
