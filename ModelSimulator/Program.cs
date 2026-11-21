using NPhysics;

if (args.Length == 0)
{
    throw new ArgumentException(
        "Informe o caminho do .nphysmodel. Opcional: --count <esferas>.",
        nameof(args));
}

var modelPath = Path.GetFullPath(args[0]);
if (!File.Exists(modelPath))
{
    throw new FileNotFoundException("Modelo neural nao encontrado.", modelPath);
}

SimulationSettings.DiskCount = 2_048;
SimulationSettings.DatasetCaptureEnabled = false;
int? maximumPhysicsSteps = null;
var presentation = SimulationPresentation.Dashboard;
for (var index = 1; index < args.Length; index++)
{
    if (args[index] == "--count"
        && index + 1 < args.Length
        && int.TryParse(args[++index], out var count)
        && count > 0)
    {
        SimulationSettings.DiskCount = count;
        continue;
    }
    if (args[index] == "--steps"
        && index + 1 < args.Length
        && int.TryParse(args[++index], out var steps)
        && steps > 0)
    {
        maximumPhysicsSteps = steps;
        continue;
    }
    if (args[index] == "--view" && index + 1 < args.Length)
    {
        presentation = SimulationPresentationParser.Parse(args[++index]);
        continue;
    }

    throw new ArgumentException(
        $"Argumento invalido: {args[index]}. Uso: <modelo> [--count N] [--steps N] [--view dashboard|scene].");
}

using var simulation = new PhysicsSimulation(modelPath, maximumPhysicsSteps, presentation);
simulation.Run();
