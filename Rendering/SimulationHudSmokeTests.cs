namespace NPhysics;

internal static class SimulationHudSmokeTests
{
    public static void Run()
    {
        var detector = new MouseButtonEdgeDetector();
        if (detector.WasPressed(false)
            || !detector.WasPressed(true)
            || detector.WasPressed(true)
            || detector.WasPressed(true)
            || detector.WasPressed(false)
            || !detector.WasPressed(true))
        {
            throw new InvalidOperationException("Clique mantido deve disparar uma unica acao por pressao.");
        }
    }
}
