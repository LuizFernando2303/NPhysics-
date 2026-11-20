using NPhysics;

var options = ParseArguments(args);
var datasetPath = options.DatasetPath ?? FindLatestDataset();
var modelPath = options.ModelPath ?? Path.ChangeExtension(datasetPath, ".nphysmodel");

Console.WriteLine($"Dataset: {datasetPath}");
Console.WriteLine($"Saida: {modelPath}");
Console.WriteLine(
    $"Configuracao: ate {options.MaxSamples:N0} amostras, minibatch de {options.BatchSize} nos.");
TrainingCommand.Run(datasetPath, modelPath, options.MaxSamples, options.BatchSize);

static (string? DatasetPath, string? ModelPath, long MaxSamples, int BatchSize) ParseArguments(
    string[] arguments)
{
    string? datasetPath = null;
    string? modelPath = null;
    var maxSamples = 25_000L;
    var batchSize = 256;
    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--dataset" when index + 1 < arguments.Length:
                datasetPath = arguments[++index];
                break;
            case "--model" when index + 1 < arguments.Length:
                modelPath = arguments[++index];
                break;
            case "--samples" when index + 1 < arguments.Length
                && long.TryParse(arguments[++index], out var samples)
                && samples > 0:
                maxSamples = samples;
                break;
            case "--batch-size" when index + 1 < arguments.Length
                && int.TryParse(arguments[++index], out var batch)
                && batch > 0:
                batchSize = batch;
                break;
            default:
                throw new ArgumentException(
                    $"Argumento invalido: {arguments[index]}. Uso: --dataset <arquivo> "
                    + "[--model <saida>] [--samples N] [--batch-size N].");
        }
    }

    return (datasetPath, modelPath, maxSamples, batchSize);
}

static string FindLatestDataset()
{
    var directory = DatasetCaptureSettings.DefaultOutputDirectory;
    if (!Directory.Exists(directory))
    {
        throw new DirectoryNotFoundException($"Diretorio de datasets nao encontrado: {directory}");
    }

    return Directory.EnumerateFiles(directory, "*.nphysdata")
        .Select(path => new FileInfo(path))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .Select(file => file.FullName)
        .FirstOrDefault()
        ?? throw new FileNotFoundException($"Nenhum .nphysdata encontrado em {directory}.");
}
