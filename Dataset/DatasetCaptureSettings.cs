namespace NPhysics;

internal sealed class DatasetCaptureSettings
{
    public string OutputDirectory { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NPhysics",
            "datasets");
    public int DiskCount { get; init; } = SimulationSettings.DiskCount;
    public int Seed { get; init; } = 2026;
    public int CaptureFrequency { get; init; } = 10;
    public int MaxSamples { get; init; } = 1_000_000;
    public int MaxNeighbors { get; init; } = 16;
    public bool DisableObstacles { get; init; } = true;
    public float DurationSeconds { get; init; } = 60f;
}
