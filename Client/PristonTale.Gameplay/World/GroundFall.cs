using PristonTale.Engine3D.Formats;

namespace PristonTale.Gameplay.World;

// The original's fall: a player more than 8 units above its floor drops 8 units a frame at 70 frames a second; a drop of
// more than 32 shows the fall motion, and landing after more than 200 shows the hard landing instead of the soft one.
public static class GroundFall
{
    public const float SnapDistance = 8f;
    public const float MotionDistance = 32f;
    public const float HardLandingDistance = 200f;
    public const float Speed = 8f * 70f;

    public static bool IsAirborne(float feet, float floor) => feet - floor > SnapDistance;

    // The feet height after dt seconds of falling, never below the floor.
    public static float Step(float feet, float floor, float dt) => MathF.Max(floor, feet - Speed * dt);

    public static PristonActorMotion Landing(float fallen) =>
        fallen > HardLandingDistance ? PristonActorMotion.FallDamage : PristonActorMotion.FallStand;
}
