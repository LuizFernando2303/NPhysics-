namespace NPhysics;

internal static class HuberLoss
{
    public static float ForwardBackward(
        float[] prediction,
        float[] target,
        float[] gradient,
        float delta = 0.1f)
    {
        var loss = 0f;
        for (var i = 0; i < prediction.Length; i++)
        {
            var difference = prediction[i] - target[i];
            var absolute = MathF.Abs(difference);
            if (absolute <= delta)
            {
                loss += 0.5f * difference * difference;
                gradient[i] = difference;
            }
            else
            {
                loss += delta * (absolute - 0.5f * delta);
                gradient[i] = delta * MathF.CopySign(1f, difference);
            }
        }
        return loss / prediction.Length;
    }
}
