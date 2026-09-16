namespace PristonTale.Engine3D.Rendering;

public sealed record PristonCameraSettings
{
    public float DefaultDistance { get; init; } = 420f;
    public float MinDistance { get; init; } = 10f;
    public float MaxDistance { get; init; } = 1200f;
    public float TargetHeight { get; init; } = 30f;
    public float DefaultPitch { get; init; } = 0.35f;
    public float MinPitch { get; init; } = -0.05f;
    public float MaxPitch { get; init; } = 1.05f;
    public float FieldOfViewDegrees { get; init; } = 65f;
    public float NearPlane { get; init; } = 1f;
    public float FarPlane { get; init; } = 20000f;
    // Exponential follow rate (higher = snappier), applied frame-rate-independently via 1 - exp(-rate*dt).
    public float FollowSmoothing { get; init; } = 14f;
    public float MouseOrbitSensitivity { get; init; } = 0.006f;
    public float MouseZoomSensitivity { get; init; } = 0.35f;
    // Eight-pixel lateral band from Winmain.cpp; angular speed is expressed per second for variable frame rates.
    public int EdgeOrbitWidth { get; init; } = 8;
    public float EdgeOrbitRadiansPerSecond { get; init; } = MathF.PI / 2f;
    // Clearance for the camera's own position/movement. Scenery between camera and player does not shorten the orbit.
    public float CollisionFloorPad { get; init; } = 32f;
    public float CollisionWallPad { get; init; } = 24f;

    public static readonly PristonCameraSettings Default = new();
}
