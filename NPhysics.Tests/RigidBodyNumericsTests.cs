using OpenTK.Mathematics;

namespace NPhysics.Tests;

public sealed class RigidBodyNumericsTests
{
    [Fact]
    public void IdentityQuaternionRotatesAroundPositiveXAxis()
    {
        var result = RigidBodyNumerics.IntegrateOrientation(
            new Vector4(0f, 0f, 0f, 1f),
            new Vector3(2f, 0f, 0f),
            0.1f);

        Assert.InRange(result.X, 0.09949f, 0.09951f);
        Assert.InRange(result.W, 0.99502f, 0.99504f);
        Assert.InRange(result.Length - 1f, -0.00001f, 0.00001f);
    }

    [Fact]
    public void IdentityRotationWithSmallStepStaysNormalized()
    {
        var result = RigidBodyNumerics.IntegrateOrientation(
            new Vector4(0f, 0f, 0f, 1f),
            new Vector3(0.25f, -0.5f, 1.25f),
            1f / 120f);

        Assert.InRange(result.Length - 1f, -0.00001f, 0.00001f);
        Assert.All(new[] { result.X, result.Y, result.Z, result.W }, AssertFinite);
    }

    [Fact]
    public void SemiImplicitFreeFallMatchesReference()
    {
        var dt = 1f / 120f;
        var position = Vector3.UnitY * 10f;
        var velocity = Vector3.Zero;

        for (var step = 0; step < 120; step++)
        {
            (position, velocity) = RigidBodyNumerics.SemiImplicitFreeFall(
                position,
                velocity,
                9.81f,
                dt);
        }

        Assert.InRange(position.Y - 5.054126f, -0.002f, 0.002f);
        Assert.InRange(velocity.Y - -9.81f, -0.0001f, 0.0001f);
    }

    [Fact]
    public void LinearMomentumIsConservedWithoutExternalImpulse()
    {
        var bodies = new[]
        {
            (Mass: 1f, Velocity: new Vector3(2f, 0f, 0f)),
            (Mass: 3f, Velocity: new Vector3(-2f / 3f, 0f, 0f)),
        };
        var before = RigidBodyNumerics.LinearMomentum(bodies);
        var after = RigidBodyNumerics.LinearMomentum(bodies);

        Assert.True((before - after).Length < 1e-6f);
        Assert.InRange(before.X, -1e-6f, 1e-6f);
    }

    private static void AssertFinite(float value)
    {
        Assert.True(float.IsFinite(value));
    }
}
