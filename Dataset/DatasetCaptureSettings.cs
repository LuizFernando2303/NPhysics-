namespace NPhysics;

internal sealed class DatasetCaptureSettings
{
    private const int DefaultCaptureFrequency = 10;
    private const long MaximumReadbackBytesPerSecond = 64L * 1024 * 1024;

    public static string DefaultOutputDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NPhysics",
            "datasets");
    public string OutputDirectory { get; init; } = DefaultOutputDirectory;
    public int DiskCount { get; init; } = SimulationSettings.DiskCount;
    public int Seed { get; init; } = 2026;
    public int CaptureFrequency { get; init; } = DefaultCaptureFrequency;
    public int MaxSamples { get; init; } = 1_000_000;
    public int MaxNeighbors { get; init; } = 16;
    public bool DisableObstacles { get; init; } = true;
    public float DurationSeconds { get; init; } = 60f;

    public static int GetCaptureFrequency(int diskCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCount);
        var bufferBytes = DatasetGenerator.CalculateCaptureBufferBytes(diskCount);
        var stepsPerSecond = 1d / SimulationSettings.FixedStep;
        var requiredFrequency = (int)Math.Ceiling(
            bufferBytes * stepsPerSecond / MaximumReadbackBytesPerSecond);
        return Math.Max(DefaultCaptureFrequency, requiredFrequency);
    }
}
