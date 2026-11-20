using OpenTK.Mathematics;

namespace NPhysics;

internal sealed class ProceduralTrack
{
    public const float HalfWidth = SimulationSettings.RampHalfWidth;
    public const int ObstacleSegmentLength = 6;
    private const float NormalSampleOffset = 0.01f;

    public float Height(float x, float z)
    {
        return -SimulationSettings.RampSlope * x
            + 0.48f * MathF.Sin(x * 0.055f)
            + 0.16f * MathF.Sin(x * 0.19f + 1.4f)
            + 0.065f * MathF.Sin(x * 0.47f)
            + 0.14f * MathF.Sin(z * 0.55f + x * 0.035f)
            + 0.045f * MathF.Sin(z * 1.25f - x * 0.11f)
            + 0.006f * z * z;
    }

    public Vector3 SurfaceNormal(float x, float z)
    {
        var slopeX = (Height(x + NormalSampleOffset, z) - Height(x - NormalSampleOffset, z))
            / (2f * NormalSampleOffset);
        var slopeZ = (Height(x, z + NormalSampleOffset) - Height(x, z - NormalSampleOffset))
            / (2f * NormalSampleOffset);
        return new Vector3(-slopeX, 1f, -slopeZ).Normalized();
    }

    public Obstacle[] CreateObstacles(float cameraX)
    {
        var firstSegment = (int)MathF.Floor((cameraX - 52f) / ObstacleSegmentLength);
        var lastSegment = (int)MathF.Ceiling((cameraX + 72f) / ObstacleSegmentLength);
        var obstacles = new List<Obstacle>(lastSegment - firstSegment + 1);

        for (var segment = firstSegment; segment <= lastSegment; segment++)
        {
            var seed = unchecked((uint)(segment * 747796405 + 2891336453));
            var x = segment * ObstacleSegmentLength + Hash01(seed) * (ObstacleSegmentLength - 1f);
            var radius = 0.58f + Hash01(seed + 1) * 0.55f;
            var isBox = Hash01(seed + 3) > 0.48f;
            var halfSize = new Vector3(
                0.55f + Hash01(seed + 4) * 0.55f,
                0.45f + Hash01(seed + 5) * 0.5f,
                0.55f + Hash01(seed + 6) * 0.55f);
            var z = (Hash01(seed + 7) * 2f - 1f) * (HalfWidth - 1f);
            var colorVariant = (int)(Hash01(seed + 8) * 4f);
            var color = colorVariant switch
            {
                0 => new Vector3(1f, 0.24f, 0.08f),
                1 => new Vector3(1f, 0.72f, 0.12f),
                2 => new Vector3(0.08f, 0.78f, 0.92f),
                _ => new Vector3(0.72f, 0.22f, 0.92f),
            };
            if (!isBox)
            {
                halfSize.Y = radius * 0.7f;
            }

            obstacles.Add(new Obstacle(
                new Vector3(x, Height(x, z) + halfSize.Y, z),
                radius,
                halfSize,
                isBox,
                color));
        }

        return obstacles.ToArray();
    }

    private static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return (value & 0x00ffffff) / 16777215f;
    }
}
