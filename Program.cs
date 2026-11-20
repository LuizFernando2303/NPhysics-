namespace NPhysics;

internal static class Program
{
    private static void Main()
    {
        using var simulation = new PhysicsSimulation();
        simulation.Run();
    }
}
