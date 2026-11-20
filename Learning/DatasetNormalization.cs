namespace NPhysics;

internal sealed class DatasetNormalization
{
    public float[] Means { get; }
    public float[] StandardDeviations { get; }

    private DatasetNormalization(float[] means, float[] standardDeviations)
    {
        Means = means;
        StandardDeviations = standardDeviations;
    }

    public static DatasetNormalization FromVectors(IEnumerable<float[]> vectors, int dimensions)
    {
        var means = new float[dimensions];
        var squares = new float[dimensions];
        var count = 0;
        foreach (var vector in vectors)
        {
            if (vector.Length != dimensions)
            {
                throw new ArgumentException("Dimensao de vetor invalida.");
            }
            count++;
            for (var i = 0; i < dimensions; i++)
            {
                means[i] += vector[i];
                squares[i] += vector[i] * vector[i];
            }
        }
        if (count == 0)
        {
            throw new InvalidDataException("Nao existem vetores para normalizacao.");
        }
        var deviations = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            means[i] /= count;
            var variance = MathF.Max(0f, squares[i] / count - means[i] * means[i]);
            deviations[i] = MathF.Sqrt(variance);
            if (deviations[i] < 1e-6f)
            {
                deviations[i] = 1f;
            }
        }
        return new DatasetNormalization(means, deviations);
    }

    public float[] Normalize(float[] vector)
    {
        var result = new float[vector.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (vector[i] - Means[i]) / StandardDeviations[i];
        }
        return result;
    }
}
