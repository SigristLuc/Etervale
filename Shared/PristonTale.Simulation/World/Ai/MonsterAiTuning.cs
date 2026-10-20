using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;

namespace PristonTale.Simulation.World.Ai;

public sealed class MonsterAiTuning
{
    public const int DefaultLeashRadius = 1280;

    public const int RunDistance = 512;

    public const int RunIqThreshold = 6;

    public const int VerticalAggroGate = 140;

    public const int StuckDistance = 32;

    public const float DecisionIntervalMs = 60f;

    public const float RetargetIntervalMs = 900f;

    public const float BossTargetLockMs = 4000f;

    // Original clip-end rules: more than 4 clips probe progress; leash and stuck set the manoeuvre count to 6 and 10.
    public const int ProgressProbeClips = 5;

    public const int LeashManoeuvreClips = 6;

    public const int StuckManoeuvreClips = 10;

    // Etervale rules near the target: approach to this much inside the reach, and keep walking instead of running
    // while the target stays within this band beyond the reach after a swing.
    public const int StopMargin = 8;

    public const int ChaseHysteresis = 24;

    public MonsterAiTuning(MonsterDefinition def, MotionTimingSet timing = null)
    {
        int sight = Math.Max(0, def.Ai.SightRadius);
        SightSq = (long)sight * sight;

        LeashSq = (long)DefaultLeashRadius * DefaultLeashRadius;

        RunDistanceSq = (long)RunDistance * RunDistance;

        // Player targets use Shooting_Range without the summon branch's padding.
        AttackRange = Math.Max(0, def.Combat.MeleeRange);
        AttackRangeSq = (long)AttackRange * AttackRange;
        StopDistance = Math.Max(0, AttackRange - StopMargin);

        // Shooting_Range 0 never passes PlayAttackFromPosi (dist < 0), and the five such definitions (doors and maple
        // event objects) have no walk or run clip, so SetMotionFromCode(WALK) fails and they never move either.
        IsImmobile = AttackRange == 0;

        StuckDistanceSq = (long)StuckDistance * StuckDistance;
        VerticalAggroGateF = VerticalAggroGate;

        CanRun = def.Ai.Iq >= RunIqThreshold;

        // Imported clips replace the fallback duration and authored hit offset.
        (WindupMs, CooldownMs) = DeriveAttackTiming(def.Combat.AttackSpeed);
        FlinchDurationMs = FlinchFallbackMs;
        DieDurationMs = 0f;

        WalkCycleMs = LocomotionCycleFallbackMs;
        RunCycleMs = LocomotionCycleFallbackMs;
        if (timing != null)
        {
            if (MonsterOffensiveSkill.IsSupported(def) && timing.TryGetClip(MotionStateCode.Skill, out MotionClip skill))
            {
                SkillDurationMs = MotionTiming.DurationMs(skill.Frames, 1f);
                SkillWindupMs = skill.TryGetEventTicks(0, out uint skillEvent)
                    ? MotionTiming.EventOffsetMs(skillEvent, 1f)
                    : SkillDurationMs;
                CanCastOffensiveSkill = SkillDurationMs > 0f;
            }

            if (timing.TryGetClip(MotionStateCode.Walk, out MotionClip walk))
            {
                WalkCycleMs = MotionTiming.DurationMs(walk.Frames, 1f);
            }

            if (timing.TryGetClip(MotionStateCode.Run, out MotionClip run))
            {
                RunCycleMs = MotionTiming.DurationMs(run.Frames, 1f);
            }

            if (timing.TryGetClip(MotionStateCode.Attack, out MotionClip attack))
            {
                CooldownMs = MotionTiming.DurationMs(attack.Frames, 1f);
                WindupMs = attack.TryGetEventTicks(0, out uint ev)
                    ? MotionTiming.EventOffsetMs(ev, 1f)
                    : CooldownMs;
            }

            if (timing.TryGetClip(MotionStateCode.Damage, out MotionClip damage))
            {
                FlinchDurationMs = MotionTiming.DurationMs(damage.Frames, MotionTiming.DamageRateFactor);
            }

            if (timing.TryGetClip(MotionStateCode.Dead, out MotionClip dead))
            {
                DieDurationMs = MotionTiming.DurationMs(dead.Frames, 1f);
            }
        }
    }

    // Explicit fallback for databases without imported locomotion clips, not a claim about every model.
    public const float LocomotionCycleFallbackMs = 1000f;
    public float WalkCycleMs { get; }
    public float RunCycleMs { get; }

    public bool CanCastOffensiveSkill { get; }
    public float SkillDurationMs { get; }
    public float SkillWindupMs { get; }

    public long SightSq { get; }
    public long LeashSq { get; }
    public long RunDistanceSq { get; }
    public long AttackRangeSq { get; }
    public int AttackRange { get; }
    public int StopDistance { get; }
    public bool IsImmobile { get; }
    public long StuckDistanceSq { get; }
    public float VerticalAggroGateF { get; }
    public bool CanRun { get; }

    public float WindupMs { get; }

    public float CooldownMs { get; }

    public float FlinchDurationMs { get; }

    public const float FlinchFallbackMs = 700f;

    public float DieDurationMs { get; }

    private static (float windup, float cooldown) DeriveAttackTiming(int attackSpeed)
    {
        // Verified play-rate factor: rate = fONE(256) + 32*clamp(AtkSpeed-6,0,6). Higher rate => faster anim.
        int clamped = Math.Clamp(attackSpeed - 6, 0, 6);
        float rate = 256f + (32f * clamped);
        const float baseDurationMs = 1000f; // Chosen slow-end fallback.
        float cooldown = baseDurationMs * (256f / rate);
        const float windupFraction = 40f / 72f; // Chosen fallback event frame.
        float windup = cooldown * windupFraction;
        return (windup, cooldown);
    }
}
