using OpenTK.Mathematics;

namespace NPhysics;

internal static class RigidBodyNumericsSmokeTests
{
    public static void Run()
    {
        var orientation = new Vector4(1f, 0f, 0f, 0f);
        var angularVelocity = new Vector3(0f, 1f, 0f);
        var next = RigidBodyNumerics.IntegrateOrientation(
            orientation,
            angularVelocity,
            0.1f);

        if (next.Z <= 0f)
        {
            throw new InvalidOperationException(
                "A derivada da orientacao deve usar cross(q_xyz, omega).");
        }
        if (MathF.Abs(next.Length - 1f) > 0.00001f)
        {
            throw new InvalidOperationException(
                "O quaternion integrado deve permanecer normalizado.");
        }

        var identity = new Vector4(0f, 0f, 0f, 1f);
        var identityNext = RigidBodyNumerics.IntegrateOrientation(
            identity,
            new Vector3(2f, 0f, 0f),
            0.1f);
        if (MathF.Abs(identityNext.X - 0.09950372f) > 0.00001f
            || MathF.Abs(identityNext.W - 0.9950372f) > 0.00001f)
        {
            throw new InvalidOperationException(
                "A integracao de orientacao nao reproduz uma rotacao X de referencia.");
        }
    }
}
