using OpenTK.Mathematics;

namespace NPhysics;

internal readonly record struct Obstacle(
    Vector3 Position,
    float Radius,
    Vector3 HalfSize,
    bool IsBox,
    Vector3 Color);
