namespace NPhysics;

internal static class SimulationSettings
{
    public const int DiskCount = 1_000_000;
    public const int WorkGroupSize = 128;
    public const int GridBucketCount = 1 << 22;
    public const float GridCellSize = 0.2f;
    public const float FixedStep = 1f / 120f;
    public const float Gravity = 9.81f;
    public const float InclinationDegrees = 45f;
    public const float MinimumRadius = 0.05f;
    public const float MaximumRadius = 0.074f;
    public const float MinimumMass = 0.2f;
    public const float MaximumMass = 2.0f;
    public const float MinimumStaticFriction = 0.35f;
    public const float MaximumStaticFriction = 0.58f;
    public const float MinimumDynamicFriction = 0.18f;
    public const float MaximumDynamicFriction = 0.32f;
    public const float MinimumRestitution = 0.1f;
    public const float MaximumRestitution = 0.75f;
    public const float SolidSphereInertiaFactor = 0.4f;
    public const float AirDensity = 1.225f;
    public const float AirDragCoefficient = 0.47f;
    public const int LaneCount = 110;
    public const int VerticalLayerCount = 6;
    public const float SpawnSpacing = 0.16f;
    public const float VerticalLayerSpacing = 0.15f;
    public const float ObstacleMeshHalfThicknessRatio = 0.12f;
    public const float RampHalfWidth = 9f;

    public static float RampSlope => MathF.Tan(InclinationDegrees * (MathF.PI / 180f));
}
