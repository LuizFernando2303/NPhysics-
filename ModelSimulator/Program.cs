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

    throw new ArgumentException($"Argumento invalido: {args[index]}.");
}

using var simulation = new PhysicsSimulation(modelPath, maximumPhysicsSteps);
simulation.Run();
