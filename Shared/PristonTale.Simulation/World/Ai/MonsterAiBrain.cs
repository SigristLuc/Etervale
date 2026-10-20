using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.World.Ai;

public sealed class MonsterAiBrain
{
    // A stuck monster turns by rand() % 1024 - 512 on the 4096-step circle (a quarter turn, centred).
    private const int StuckTurnSteps = 1024;
    private const float AngleStepRadians = MathF.Tau / 4096f;

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
                monster.ManoeuvreClipsLeft = 0;
                if (targetLive)
                {
                    monster.AiState = MonsterAiState.Chase;
                    monster.Motion = MonsterMotionState.Stand;
                    monster.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
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

        // The original target scan runs during leash and stuck manoeuvres too; only a target lock suspends it.
        if (monster.SinceRetargetMs >= MonsterAiTuning.RetargetIntervalMs && monster.TargetLockMsLeft <= 0f)
        {
            monster.SinceRetargetMs = 0f;
            monster.TargetId = AcquireNearestPrey(monster, tuning, prey);
        }

        AdvanceClips(monster, tuning, dtMs);

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
            else if (monster.AiState == MonsterAiState.Leash)
            {
                IntegrateHeading(monster, monster.ManoeuvreHeading, monster.Motion == MonsterMotionState.Run, dtMs);
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
        PreyCandidate live = default;
        bool hasTarget = m.TargetId != 0 && prey.TryGet(m.TargetId, out live) && live.IsAlive;
        if (!hasTarget)
        {
            live = default;
            m.TargetId = 0;
            m.Engaged = false;
        }

        if (t.IsImmobile)
        {
            EnterIdle(m);
            return null;
        }

        switch (m.AiState)
        {
            case MonsterAiState.Leash:
                // PlayAttackFromPosi runs on every call, so a target in reach is attacked even mid-manoeuvre.
                if (hasTarget && MonsterAttackReach.CanBegin(m.Def, m.Position, live.Position))
                {
                    FaceToward(m, live.Position);
                    BeginAttack(m, t);
                }
                else
                {
                    IntegrateHeading(m, m.ManoeuvreHeading, m.Motion == MonsterMotionState.Run, decisionDt);
                }

                return null;

            case MonsterAiState.Idle:
            case MonsterAiState.Wander:
                if (hasTarget)
                {
                    m.AiState = MonsterAiState.Chase;
                    return StepChase(m, t, live, decisionDt);
                }

                StepWander(m, t, decisionDt);
                return null;

            case MonsterAiState.Chase:
                if (!hasTarget)
                {
                    EnterIdle(m);
                    return null;
                }

                return StepChase(m, t, live, decisionDt);

            default:
                return null;
        }
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

    private static void EnterIdle(MonsterInstance m)
    {
        m.AiState = MonsterAiState.Idle;
        m.Motion = MonsterMotionState.Stand;
        m.TargetId = 0;
        m.Engaged = false;
        m.ManoeuvreClipsLeft = 0;
    }

    private static void EndManoeuvre(MonsterInstance m)
    {
        m.ManoeuvreClipsLeft = 0;
        m.Motion = MonsterMotionState.Stand;
        if (m.TargetId != 0)
        {
            m.AiState = MonsterAiState.Chase;
            m.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
        }
        else
        {
            EnterIdle(m);
        }
    }

    private MonsterAttackEvent? StepChase(MonsterInstance m, MonsterAiTuning t, PreyCandidate target, float dt)
    {
        if (MonsterAttackReach.CanBegin(m.Def, m.Position, target.Position))
        {
            FaceToward(m, target.Position);
            BeginAttack(m, t);
            return null;
        }

        float dx = target.Position.X - m.Position.X;
        float dz = target.Position.Z - m.Position.Z;
        float distSq = (dx * dx) + (dz * dz);
        float dist = MathF.Sqrt(distSq);
        if (m.Engaged && dist > t.AttackRange + MonsterAiTuning.ChaseHysteresis)
        {
            m.Engaged = false;
        }

        // Approach a point inside the reach on the line to the target instead of the target itself; there the next
        // decision swings. A target the monster cannot swing at from there (height) is faced while standing.
        if (dist <= t.StopDistance + 0.01f)
        {
            FaceToward(m, target.Position);
            m.Motion = MonsterMotionState.Stand;
            return null;
        }

        bool run = !m.Engaged && t.CanRun && distSq < t.RunDistanceSq;
        float approach = (dist - t.StopDistance) / dist;
        m.MoveTarget = new Vector3(m.Position.X + dx * approach, m.Position.Y, m.Position.Z + dz * approach);
        m.Motion = run ? MonsterMotionState.Run : MonsterMotionState.Walk;
        IntegrateToward(m, m.MoveTarget, run, dt);
        return null;
    }

    private static void StepWander(MonsterInstance m, MonsterAiTuning t, float dt)
    {
        if (m.AiState == MonsterAiState.Idle)
        {
            return;
        }

        if (t.IsImmobile)
        {
            EnterIdle(m);
            return;
        }

        // A new monster starts with a manoeuvre count of 2. Without prey, walk in the spawn direction for two clips, then stand.
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

    // Clip-end bookkeeping of the original character update for a monster with a target or a manoeuvre: the progress probe, the
    // manoeuvre count and the leash, in the original order.
    private void AdvanceClips(MonsterInstance m, MonsterAiTuning t, float dtMs)
    {
        bool counting = !t.IsImmobile && m.AiState is MonsterAiState.Chase or MonsterAiState.Leash;
        if (!counting)
        {
            m.ClipProgress = 0f;
            m.ProbeClips = 0;
            m.CrowdedSinceProbe = false;
            m.ProgressProbePosition = m.Position;
            return;
        }

        m.CrowdedSinceProbe |= m.Crowded;
        float cycleMs = m.Motion == MonsterMotionState.Run ? t.RunCycleMs : t.WalkCycleMs;
        m.ClipProgress += dtMs / MathF.Max(1f, cycleMs);
        while (m.ClipProgress >= 1f && m.AiState is MonsterAiState.Chase or MonsterAiState.Leash)
        {
            m.ClipProgress -= 1f;
            EndClip(m, t);
        }
    }

    private void EndClip(MonsterInstance m, MonsterAiTuning t)
    {
        m.ProbeClips++;
        if (m.ProbeClips >= MonsterAiTuning.ProgressProbeClips)
        {
            bool stuck = PlanarDistanceSq(m.Position, m.ProgressProbePosition) < t.StuckDistanceSq;
            m.ProgressProbePosition = m.Position;
            m.ProbeClips = 0;
            bool queued = m.CrowdedSinceProbe;
            m.CrowdedSinceProbe = false;
            if (stuck && !queued)
            {
                BeginStuckManoeuvre(m, t);
                return;
            }
        }

        if (m.AiState == MonsterAiState.Leash)
        {
            m.ManoeuvreClipsLeft--;
            if (m.ManoeuvreClipsLeft <= 0)
            {
                EndManoeuvre(m);
            }
        }

        // Soft leash: head home for six clips, keeping the target; the chase may resume afterwards.
        if (m.Motion.IsMoving() && PlanarDistanceSq(m.Position, m.HomeAnchor) > t.LeashSq)
        {
            BeginManoeuvre(m, HeadingTo(m, m.HomeAnchor), MonsterAiTuning.LeashManoeuvreClips, m.Motion);
        }
    }

    // Stuck: turn up to 45 degrees off the way home and run for ten clips without dropping the target.
    private void BeginStuckManoeuvre(MonsterInstance m, MonsterAiTuning t)
    {
        int turn = _rng.NextExclusive(0, StuckTurnSteps) - (StuckTurnSteps / 2);
        float heading = HeadingTo(m, m.HomeAnchor) + (turn * AngleStepRadians);
        var motion = t.CanRun ? MonsterMotionState.Run : MonsterMotionState.Walk;
        BeginManoeuvre(m, heading, MonsterAiTuning.StuckManoeuvreClips, motion);
    }

    private static void BeginManoeuvre(MonsterInstance m, float heading, int clips, MonsterMotionState motion)
    {
        m.AiState = MonsterAiState.Leash;
        m.Motion = motion;
        m.Engaged = false;
        m.ManoeuvreClipsLeft = clips;
        m.ManoeuvreHeading = heading;
        m.Facing = heading;
    }

    private void BeginAttack(MonsterInstance m, MonsterAiTuning tuning)
    {
        m.AiState = MonsterAiState.Attack;
        m.Motion = MonsterMotionState.Attack;
        m.Engaged = true;
        m.ManoeuvreClipsLeft = 0;
        m.AttackTimerMs = 0f;
        m.ClipProgress = 0f;
        m.ProbeClips = 0;
        m.CrowdedSinceProbe = false;
        m.ProgressProbePosition = m.Position;
        m.AttackHitFired = false;
        m.SwingJustBegan = true;

        // Skill roll on attack entry: rand()%100 < SkillRating.
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

    private MonsterAttackEvent? AdvanceAttack(MonsterInstance m, MonsterAiTuning t, IPreyView prey, float dtMs)
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
            PreyCandidate tgt2 = default;
            bool stillValid = m.TargetId != 0 && prey.TryGet(m.TargetId, out tgt2) && tgt2.IsAlive;
            // The clip ends in STAND and PlayAttackFromPosi swings again in the same call while the target stays in
            // reach, so consecutive attacks never pass through a walk. An impact on this tick is resolved first.
            if (stillValid && landed is null && MonsterAttackReach.CanBegin(m.Def, m.Position, tgt2.Position))
            {
                FaceToward(m, tgt2.Position);
                BeginAttack(m, t);
            }
            else if (stillValid)
            {
                m.AiState = MonsterAiState.Chase;
                m.Motion = MonsterMotionState.Stand;
                m.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
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
        monster.ProbeClips = 0;
        monster.ClipProgress = 0f;
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
            boss.ManoeuvreClipsLeft = 0;
            if (boss.Motion != MonsterMotionState.Damage)
            {
                boss.Motion = MonsterMotionState.Stand;
            }

            boss.SinceDecisionMs = MonsterAiTuning.DecisionIntervalMs;
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

    // Manoeuvres keep the heading chosen when they began (MoveAngle along Angle.y); walls are slid by the host.
    private static void IntegrateHeading(MonsterInstance m, float heading, bool run, float dtMs)
    {
        float step = MonsterLocomotion.UnitsPerSecond(m.Def, run) * (dtMs / 1000f);
        var direction = new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));
        m.Facing = heading;
        m.MoveTarget = m.Position + direction * 10000f;
        m.Position += direction * step;
    }

    private static float HeadingTo(MonsterInstance m, Vector3 point)
    {
        float dx = point.X - m.Position.X;
        float dz = point.Z - m.Position.Z;
        return (dx * dx) + (dz * dz) < 1e-4f ? m.Facing : MathF.Atan2(dx, dz);
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
