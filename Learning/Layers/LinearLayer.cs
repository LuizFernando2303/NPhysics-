namespace NPhysics;

internal sealed class LinearLayer
{
    private float[] _input = [];

    public LinearLayer(int inputSize, int outputSize, int seed)
    {
        InputSize = inputSize;
        OutputSize = outputSize;
        Weight = new float[outputSize * inputSize];
        Bias = new float[outputSize];
        var random = new Random(seed);
        var limit = MathF.Sqrt(6f / (inputSize + outputSize));
        for (var i = 0; i < Weight.Length; i++)
        {
            Weight[i] = (float)(random.NextDouble() * 2.0 - 1.0) * limit;
        }
        WeightGradient = new float[Weight.Length];
        BiasGradient = new float[Bias.Length];
    }

    public int InputSize { get; }
    public int OutputSize { get; }
    public float[] Weight { get; }
    public float[] Bias { get; }
    public float[] WeightGradient { get; }
    public float[] BiasGradient { get; }

    public float[] Forward(float[] input)
    {
        if (input.Length != InputSize)
        {
            throw new ArgumentException("Entrada incompatível com a camada linear.");
        }
        _input = input;
        var output = new float[OutputSize];
        for (var row = 0; row < OutputSize; row++)
        {
            var sum = Bias[row];
            var weightOffset = row * InputSize;
            for (var col = 0; col < InputSize; col++)
            {
                sum += Weight[weightOffset + col] * input[col];
            }
            output[row] = sum;
        }
        return output;
    }

    public float[] Backward(float[] outputGradient)
    {
        if (outputGradient.Length != OutputSize)
        {
            throw new ArgumentException("Gradiente incompatível com a camada linear.");
        }
        if (WeightGradient.Length != Weight.Length)
        {
            throw new InvalidOperationException("Gradientes da camada nao foram inicializados.");
        }
        var inputGradient = new float[InputSize];
        for (var row = 0; row < OutputSize; row++)
        {
            var gradient = outputGradient[row];
            BiasGradient[row] += gradient;
            var weightOffset = row * InputSize;
            for (var col = 0; col < InputSize; col++)
            {
                WeightGradient[weightOffset + col] += gradient * _input[col];
                inputGradient[col] += Weight[weightOffset + col] * gradient;
            }
        }
        return inputGradient;
    }

    public float[] Backward(float[] input, float[] outputGradient)
    {
        if (input.Length != InputSize || outputGradient.Length != OutputSize)
        {
            throw new ArgumentException("Dimensao invalida no backprop da camada.");
        }
        var inputGradient = new float[InputSize];
        for (var row = 0; row < OutputSize; row++)
        {
            var gradient = outputGradient[row];
            BiasGradient[row] += gradient;
            var weightOffset = row * InputSize;
            for (var col = 0; col < InputSize; col++)
            {
                WeightGradient[weightOffset + col] += gradient * input[col];
                inputGradient[col] += Weight[weightOffset + col] * gradient;
            }
        }
        return inputGradient;
    }

    public void ZeroGradients()
    {
        Array.Clear(WeightGradient);
        Array.Clear(BiasGradient);
    }

    public void ApplyGradients(float scale)
    {
        if (float.IsFinite(scale) == false)
        {
            throw new InvalidOperationException("Escala de gradiente invalida.");
        }
        for (var i = 0; i < Weight.Length; i++)
        {
            Weight[i] -= scale * WeightGradient[i];
        }
        for (var i = 0; i < Bias.Length; i++)
        {
            Bias[i] -= scale * BiasGradient[i];
        }
    }
}
