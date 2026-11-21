using OpenTK.Mathematics;

namespace NPhysics;

internal static class RigidBodyNumerics
{
    public static (Vector3 Position, Vector3 Velocity) SemiImplicitFreeFall(
        Vector3 position,
        Vector3 velocity,
        float gravity,
        float deltaTime)
    {
        if (!float.IsFinite(gravity) || gravity < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(gravity));
        }
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }
        var nextVelocity = velocity - Vector3.UnitY * gravity * deltaTime;
        var nextPosition = position + nextVelocity * deltaTime;
        return (nextPosition, nextVelocity);
    }

    public static Vector3 LinearMomentum(IEnumerable<(float Mass, Vector3 Velocity)> bodies)
    {
        var momentum = Vector3.Zero;
        foreach (var (mass, velocity) in bodies)
        {
            if (!float.IsFinite(mass) || mass <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(bodies));
            }
            momentum += mass * velocity;
        }
        return momentum;
    }

    public static Vector4 IntegrateOrientation(
        Vector4 orientation,
        Vector3 angularVelocity,
        float deltaTime)
    {
        var derivative = new Vector4(
            Vector3.Cross(orientation.Xyz, angularVelocity)
                + orientation.W * angularVelocity,
            -Vector3.Dot(angularVelocity, orientation.Xyz));
        var integrated = orientation + 0.5f * deltaTime * derivative;
        var magnitude = integrated.Length;
        return magnitude > 0.000001f
            ? integrated / magnitude
            : new Vector4(0f, 0f, 0f, 1f);
    }
}
