namespace NPhysics;

internal readonly record struct TelemetryFrameWindow(
    int FrameCount,
    int PhysicsStepCount,
    int ObstacleCount,
    float CameraX,
    bool Focused,
    bool Paused,
    double AverageFrameMilliseconds,
    double AveragePhysicsSubmissionMilliseconds,
    double AverageRenderSubmissionMilliseconds);

internal readonly record struct GpuTelemetrySample(
    long CaptureId,
    DateTime CapturedAtUtc,
    double IntervalSeconds,
    TelemetryFrameWindow FrameWindow,
    uint CandidateChecks,
    uint DiskContacts,
    uint RampContacts,
    uint ObstacleContacts,
    uint GridBucketConflicts,
    uint InvalidStates,
    float MaximumSpeed,
    float MaximumAngularSpeedRadiansPerSecond,
    uint VisibleDiskFrames,
    double LastGridClearGpuMilliseconds,
    double LastGridBuildGpuMilliseconds,
    double LastSimulationGpuMilliseconds,
    double LastCullingGpuMilliseconds,
    double LastDrawingGpuMilliseconds);
