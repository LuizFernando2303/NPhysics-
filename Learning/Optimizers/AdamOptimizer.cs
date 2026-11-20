namespace NPhysics;

internal sealed class AdamOptimizer
{
    private readonly float _learningRate;
    private readonly float _beta1;
    private readonly float _beta2;
    private readonly float _epsilon;
    private readonly Dictionary<(LinearLayer Layer, bool Bias), float[]> _firstMoments = [];
    private readonly Dictionary<(LinearLayer Layer, bool Bias), float[]> _secondMoments = [];
    private int _step;

    public AdamOptimizer(float learningRate = 1e-3f)
    {
        _learningRate = learningRate;
        _beta1 = 0.9f;
        _beta2 = 0.999f;
        _epsilon = 1e-8f;
    }

    public void Step(IEnumerable<LinearLayer> layers)
    {
        _step++;
        var correction1 = 1f - MathF.Pow(_beta1, _step);
        var correction2 = 1f - MathF.Pow(_beta2, _step);
        foreach (var layer in layers)
        {
            Apply(layer.Weight, layer.WeightGradient, layer, false);
            Apply(layer.Bias, layer.BiasGradient, layer, true);
        }

        void Apply(float[] values, float[] gradients, LinearLayer layer, bool isBias)
        {
            var key = (layer, isBias);
            if (!_firstMoments.TryGetValue(key, out var moments))
            {
                moments = new float[values.Length];
                _firstMoments[key] = moments;
            }
            if (!_secondMoments.TryGetValue(key, out var variances))
            {
                variances = new float[values.Length];
                _secondMoments[key] = variances;
            }
            for (var i = 0; i < values.Length; i++)
            {
                moments[i] = _beta1 * moments[i] + (1f - _beta1) * gradients[i];
                variances[i] = _beta2 * variances[i] + (1f - _beta2) * gradients[i] * gradients[i];
                var adjustedMoment = moments[i] / correction1;
                var adjustedVariance = variances[i] / correction2;
                values[i] -= _learningRate * adjustedMoment
                    / (MathF.Sqrt(adjustedVariance) + _epsilon);
            }
        }
    }
}
