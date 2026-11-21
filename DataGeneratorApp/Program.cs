using NPhysics;

var (diskCount, seed, maximumPhysicsSteps, presentation) = ParseArguments(args);
SimulationSettings.DiskCount = diskCount;
SimulationSettings.Seed = seed;
SimulationSettings.DatasetCaptureEnabled = true;

Console.WriteLine($"Geracao de dataset ativada para {diskCount:N0} esferas (seed {seed}).");
using var simulation = new PhysicsSimulation(
    maximumPhysicsSteps: maximumPhysicsSteps,
    presentation: presentation);
simulation.Run();

static (int DiskCount, int Seed, int? MaximumPhysicsSteps, SimulationPresentation Presentation) ParseArguments(
    string[] arguments)
{
    var diskCount = SimulationSettings.DiskCount;
    var seed = SimulationSettings.Seed;
    int? maximumPhysicsSteps = null;
    var presentation = SimulationPresentation.Dashboard;
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
            case "--view" when index + 1 < arguments.Length:
                presentation = SimulationPresentationParser.Parse(arguments[++index]);
                break;
            default:
                throw new ArgumentException(
                    $"Argumento invalido: {arguments[index]}. "
                    + "Uso: [--count N] [--seed N] [--steps N] [--view dashboard|scene].");
        }
    }

    return (diskCount, seed, maximumPhysicsSteps, presentation);
}
