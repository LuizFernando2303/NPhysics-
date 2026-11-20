using System.Text.Json;

namespace NPhysics;

internal sealed class SimulationTrace : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StreamWriter _writer;

    public SimulationTrace(
        string graphicsVendor,
        string graphicsRenderer,
        string graphicsVersion,
        bool detailedGpuCountersAvailable,
        string solverBackend = "teacher",
        string? modelPath = null)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NPhysics",
            "traces");
        Directory.CreateDirectory(directory);

        var fileName = $"simulation-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.jsonl";
        FilePath = Path.Combine(directory, fileName);
        _writer = new StreamWriter(new FileStream(
            FilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read));

        Write(new
        {
            type = "session",
            schemaVersion = 1,
            startedAtUtc = DateTime.UtcNow,
            graphicsVendor,
            graphicsRenderer,
            graphicsVersion,
            solverBackend,
            modelPath,
            detailedGpuCountersAvailable,
            diskCount = SimulationSettings.DiskCount,
            fixedStepSeconds = SimulationSettings.FixedStep,
            gravityMetersPerSecondSquared = SimulationSettings.Gravity,
            inclinationDegrees = SimulationSettings.InclinationDegrees,
            gridCellSizeMeters = SimulationSettings.GridCellSize,
            gridBucketCount = SimulationSettings.GridBucketCount,
            minimumRadiusMeters = SimulationSettings.MinimumRadius,
            maximumRadiusMeters = SimulationSettings.MaximumRadius,
            minimumMassKilograms = SimulationSettings.MinimumMass,
            maximumMassKilograms = SimulationSettings.MaximumMass,
            minimumStaticFriction = SimulationSettings.MinimumStaticFriction,
            maximumStaticFriction = SimulationSettings.MaximumStaticFriction,
            minimumDynamicFriction = SimulationSettings.MinimumDynamicFriction,
            maximumDynamicFriction = SimulationSettings.MaximumDynamicFriction,
            minimumRestitution = SimulationSettings.MinimumRestitution,
            maximumRestitution = SimulationSettings.MaximumRestitution,
            bodyShape = "solidSphere",
            sphereInertiaFactor = SimulationSettings.SolidSphereInertiaFactor,
            airDensityKilogramsPerCubicMeter = SimulationSettings.AirDensity,
            airDragCoefficient = SimulationSettings.AirDragCoefficient,
            verticalLayerSpacingMeters = SimulationSettings.VerticalLayerSpacing,
        });
    }

    public string FilePath { get; }
    public bool IsEnabled { get; private set; } = true;

    public void WriteFrameCapture(
        int frameNumber,
        string imagePath,
        int width,
        int height,
        double simulationTimeSeconds,
        float cameraX,
        bool paused,
        bool focused)
    {
        Write(new
        {
            type = "frame",
            timestampUtc = DateTime.UtcNow,
            frameNumber,
            imagePath,
            width,
            height,
            simulationTimeSeconds,
            cameraX,
            paused,
            focused,
        });
    }

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;
        Write(new
        {
            type = "capture",
            timestampUtc = DateTime.UtcNow,
            enabled,
        });
    }

    public void WriteEvent(string eventName, float cameraX, bool paused)
    {
        if (!IsEnabled)
        {
            return;
        }

        Write(new
        {
            type = "event",
            timestampUtc = DateTime.UtcNow,
            name = eventName,
            cameraX,
            paused,
        });
    }

    public void WriteSample(GpuTelemetrySample sample)
    {
        if (!IsEnabled)
        {
            return;
        }

        Write(new
        {
            type = "sample",
            timestampUtc = sample.CapturedAtUtc,
            sample.CaptureId,
            sample.IntervalSeconds,
            sample.FrameWindow.FrameCount,
            sample.FrameWindow.PhysicsStepCount,
            sample.FrameWindow.ObstacleCount,
            sample.FrameWindow.CameraX,
            sample.FrameWindow.Focused,
            sample.FrameWindow.Paused,
            sample.FrameWindow.AverageFrameMilliseconds,
            sample.FrameWindow.AveragePhysicsSubmissionMilliseconds,
            sample.FrameWindow.AverageRenderSubmissionMilliseconds,
            sample.CandidateChecks,
            sample.DiskContacts,
            sample.RampContacts,
            sample.ObstacleContacts,
            sample.GridBucketConflicts,
            sample.InvalidStates,
            sample.MaximumSpeed,
            sample.MaximumAngularSpeedRadiansPerSecond,
            sample.VisibleDiskFrames,
            sample.LastGridClearGpuMilliseconds,
            sample.LastGridBuildGpuMilliseconds,
            sample.LastSimulationGpuMilliseconds,
            sample.LastCullingGpuMilliseconds,
            sample.LastDrawingGpuMilliseconds,
        });
    }

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }

    private void Write<T>(T value)
    {
        _writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        _writer.Flush();
    }
}
