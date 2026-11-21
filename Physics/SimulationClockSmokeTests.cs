namespace NPhysics;

internal static class SimulationClockSmokeTests
{
    public static void Run()
    {
        const float fixedStep = 1f / 120f;
        var clock = new SimulationClock(fixedStep, maximumStepsPerFrame: 8);

        if (clock.ConsumeSteps(fixedStep * 16f) != 8
            || clock.AccumulatorSeconds < 0f
            || clock.AccumulatorSeconds >= fixedStep
            || clock.DroppedTimeSeconds <= 0d)
        {
            throw new InvalidOperationException(
                "O relogio deve limitar a recuperacao e contabilizar tempo descartado.");
        }

        clock.SetPaused(true);
        var pausedTime = clock.SimulationTimeSeconds;
        if (clock.ConsumeSteps(1f) != 0
            || clock.AccumulatorSeconds != 0f
            || clock.SimulationTimeSeconds != pausedTime)
        {
            throw new InvalidOperationException(
                "Pausa deve congelar o acumulador e o tempo fisico.");
        }

        clock.SetPaused(false);
        clock.Reset();
        if (clock.ConsumeSteps(fixedStep * 120f) != 8
            || clock.SimulationTimeSeconds <= 0d)
        {
            throw new InvalidOperationException(
                "A recuperacao apos reset deve continuar limitada e reproduzivel.");
        }

        clock.SetTimeScale(0f);
        var stepsAtZeroScale = clock.ConsumeSteps(1f);
        if (stepsAtZeroScale != 0)
        {
            throw new InvalidOperationException(
                "Escala de tempo zero deve impedir passos fisicos.");
        }
    }
}
