using System.Text.Json;
using System.Text.Json.Serialization;

namespace NPhysics;

internal sealed record ExperimentConfiguration
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public int SchemaVersion { get; init; } = CurrentVersion;
    public int DiskCount { get; init; } = SimulationSettings.DiskCount;
    public int Seed { get; init; } = SimulationSettings.Seed;
    public bool DatasetCaptureEnabled { get; init; } = SimulationSettings.DatasetCaptureEnabled;
    public string? NeuralModelPath { get; init; }
    public int? MaximumPhysicsSteps { get; init; }

    [JsonIgnore]
    public string? SourcePath { get; init; }

    public static ExperimentConfiguration Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        var configuration = JsonSerializer.Deserialize<ExperimentConfiguration>(stream, ReadOptions)
            ?? throw new InvalidDataException("Configuracao de experimento vazia.");
        configuration.Validate();
        var normalizedModelPath = configuration.NeuralModelPath is null
            ? null
            : Path.GetFullPath(configuration.NeuralModelPath);
        return configuration with
        {
            SourcePath = Path.GetFullPath(filePath),
            NeuralModelPath = normalizedModelPath,
        };
    }

    public static ExperimentConfiguration Create()
    {
        return new ExperimentConfiguration
        {
            DiskCount = SimulationSettings.DiskCount,
            Seed = SimulationSettings.Seed,
            DatasetCaptureEnabled = SimulationSettings.DatasetCaptureEnabled,
        };
    }

    public void Save(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Validate();
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, this with { SourcePath = null }, WriteOptions);
    }

    public void Apply()
    {
        Validate();
        SimulationSettings.DiskCount = DiskCount;
        SimulationSettings.Seed = Seed;
        SimulationSettings.DatasetCaptureEnabled = DatasetCaptureEnabled;
    }

    private void Validate()
    {
        if (SchemaVersion != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Versao de experimento nao suportada: {SchemaVersion}; esperada {CurrentVersion}.");
        }
        if (DiskCount <= 0)
        {
            throw new InvalidDataException("DiskCount deve ser positivo.");
        }
        if (MaximumPhysicsSteps <= 0)
        {
            throw new InvalidDataException("MaximumPhysicsSteps deve ser positivo.");
        }
        if (NeuralModelPath is not null)
        {
            var fullPath = Path.GetFullPath(NeuralModelPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Modelo neural nao encontrado.", fullPath);
            }
        }
    }
}
