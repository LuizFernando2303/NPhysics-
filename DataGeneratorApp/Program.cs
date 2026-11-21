using NPhysics;

var (diskCount, seed, maximumPhysicsSteps) = ParseArguments(args);
SimulationSettings.DiskCount = diskCount;
SimulationSettings.Seed = seed;
SimulationSettings.DatasetCaptureEnabled = true;

Console.WriteLine($"Geracao de dataset ativada para {diskCount:N0} esferas (seed {seed}).");
using var simulation = new PhysicsSimulation(maximumPhysicsSteps: maximumPhysicsSteps);
simulation.Run();

static (int DiskCount, int Seed, int? MaximumPhysicsSteps) ParseArguments(string[] arguments)
{
    var diskCount = SimulationSettings.DiskCount;
    var seed = SimulationSettings.Seed;
    int? maximumPhysicsSteps = null;
    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--count" when index + 1 < arguments.Length
                && int.TryParse(arguments[++index], out var count)
                && count > 0:
                diskCount = count;
                break;
            case "--seed" when index + 1 < arguments.Length
                && int.TryParse(arguments[++index], out var parsedSeed):
                seed = parsedSeed;
                break;
            case "--steps" when index + 1 < arguments.Length
                && int.TryParse(arguments[++index], out var steps)
                && steps > 0:
                maximumPhysicsSteps = steps;
                break;
            default:
                throw new ArgumentException(
                    $"Argumento invalido: {arguments[index]}. "
                    + "Uso: [--count N] [--seed N] [--steps N].");
        }
    }

    return (diskCount, seed, maximumPhysicsSteps);
}
