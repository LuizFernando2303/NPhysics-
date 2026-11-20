namespace NPhysics;

internal sealed class TrainingSettings
{
    public int EpochCount { get; init; } = 10;
    public int BatchSize { get; init; } = 64;
    public float LearningRate { get; init; } = 1e-3f;
    public float ValidationFraction { get; init; } = 0.15f;
    public float TestFraction { get; init; } = 0.15f;
}
