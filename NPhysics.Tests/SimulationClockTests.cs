namespace NPhysics.Tests;

public sealed class SimulationClockTests
{
    [Fact]
    public void CapturesCatchesUpButDropsExcessTime()
    {
        const float fixedStep = 1f / 120f;
        var clock = new SimulationClock(fixedStep, maximumStepsPerFrame: 8);

        var steps = clock.ConsumeSteps(fixedStep * 16f);

        Assert.Equal(8, steps);
        Assert.InRange(clock.AccumulatorSeconds, 0f, fixedStep);
        Assert.True(clock.DroppedTimeSeconds > 0d);
        Assert.Equal(8 * fixedStep, clock.SimulationTimeSeconds, 6);
    }

    [Fact]
    public void PauseAndResetAreStable()
    {
        var clock = new SimulationClock(1f / 120f);
        clock.ConsumeSteps(0.05f);
        clock.SetPaused(true);
        var simulationTime = clock.SimulationTimeSeconds;

        Assert.Equal(0, clock.ConsumeSteps(1f));
        Assert.Equal(simulationTime, clock.SimulationTimeSeconds);
        Assert.Equal(0f, clock.AccumulatorSeconds);

        clock.SetPaused(false);
        clock.Reset();
        Assert.Equal(0d, clock.SimulationTimeSeconds);
        Assert.Equal(0d, clock.DroppedTimeSeconds);
    }

    [Fact]
    public void ZeroTimeScaleProducesNoPhysicsSteps()
    {
        var clock = new SimulationClock(1f / 120f);
        clock.SetTimeScale(0f);

        Assert.Equal(0, clock.ConsumeSteps(1f));
        Assert.Equal(0d, clock.SimulationTimeSeconds);
    }
}
