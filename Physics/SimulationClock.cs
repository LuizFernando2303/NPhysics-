namespace NPhysics;

internal sealed class SimulationClock
{
    public SimulationClock(float fixedStepSeconds, int maximumStepsPerFrame = 8)
    {
        if (!float.IsFinite(fixedStepSeconds) || fixedStepSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds));
        }
        if (maximumStepsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumStepsPerFrame));
        }

        FixedStepSeconds = fixedStepSeconds;
        MaximumStepsPerFrame = maximumStepsPerFrame;
    }

    public float FixedStepSeconds { get; }
    public int MaximumStepsPerFrame { get; }
    public float TimeScale { get; private set; } = 1f;
    public float AccumulatorSeconds { get; private set; }
    public double SimulationTimeSeconds { get; private set; }
    public double DroppedTimeSeconds { get; private set; }
    public bool Paused { get; private set; }

    public void SetPaused(bool paused)
    {
        Paused = paused;
        AccumulatorSeconds = 0f;
    }

    public void SetTimeScale(float timeScale)
    {
        if (!float.IsFinite(timeScale) || timeScale < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(timeScale));
        }
        TimeScale = timeScale;
    }

    public int ConsumeSteps(float realFrameSeconds)
    {
        if (Paused)
        {
            return 0;
        }
        if (!float.IsFinite(realFrameSeconds) || realFrameSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(realFrameSeconds));
        }

        AccumulatorSeconds += realFrameSeconds * TimeScale;
        var stepCount = 0;
        while (AccumulatorSeconds + 1e-7f >= FixedStepSeconds
            && stepCount < MaximumStepsPerFrame)
        {
            AccumulatorSeconds -= FixedStepSeconds;
            SimulationTimeSeconds += FixedStepSeconds;
            stepCount++;
        }

        if (stepCount == MaximumStepsPerFrame
            && AccumulatorSeconds + 1e-7f >= FixedStepSeconds)
        {
            DroppedTimeSeconds += AccumulatorSeconds;
            AccumulatorSeconds = 0f;
        }
        return stepCount;
    }

    public void Reset()
    {
        AccumulatorSeconds = 0f;
        SimulationTimeSeconds = 0d;
        DroppedTimeSeconds = 0d;
    }
}
