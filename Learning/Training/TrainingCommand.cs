using System.Globalization;

namespace NPhysics;

internal static class TrainingCommand
{
    public static void Run(
        string datasetPath,
        string? outputModelPath = null,
        long maxSamples = 25_000,
        int batchSize = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var overview = TrainingDashboard.AnalyzeDataset(datasetPath);
        var modelPath = Path.GetFullPath(
            outputModelPath ?? Path.ChangeExtension(overview.FilePath, ".nphysmodel"));
        var reportPath = Path.ChangeExtension(modelPath, ".training.html");
        var results = new List<TrainingBlockResult>();
        var network = new GraphNetwork();
        var trainer = new GraphTrainer(network, new AdamOptimizer(1e-3f));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var expectedSamples = Math.Min(
            maxSamples,
            Math.Max(0, overview.SampleCount - overview.OverflowSampleCount));
        var progressInterval = Math.Max(1, (int)Math.Ceiling(expectedSamples / (double)batchSize / 100));
        long trainedSamples = 0;
        TrainingDashboard.WriteReport(
            reportPath,
            overview,
            "Treinando",
            results,
            stopwatch.Elapsed,
            trainingSampleLimit: maxSamples);

        try
        {
            foreach (var frame in DatasetLoader.Read(overview.FilePath, batchSize, maxSamples))
            {
                var loss = trainer.TrainSample(
                    frame.NodeInput,
                    frame.EdgeFlat,
                    frame.EdgeCounts,
                    frame.EdgeTargets,
                    frame.Target);
                trainedSamples += frame.EdgeCounts.Length;
                results.Add(new TrainingBlockResult(results.Count + 1, frame.EdgeCounts.Length, loss));
                if (results.Count % progressInterval == 0 || trainedSamples >= expectedSamples)
                {
                    TrainingDashboard.WriteReport(
                        reportPath,
                        overview,
                        "Treinando",
                        results,
                        stopwatch.Elapsed,
                        trainingSampleLimit: maxSamples);
                    Console.WriteLine(
                        $"Treinamento {trainedSamples:N0}/{expectedSamples:N0} amostras, "
                        + $"minibatches={results.Count:N0}, loss={loss:0.000000}");
                }
            }

            if (trainedSamples == 0)
            {
                throw new InvalidDataException(
                    "Nenhuma amostra utilizavel: todas as amostras possuem overflow de vizinhos.");
            }

            var meanLoss = results.Average(result => result.Loss);
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            ModelSerializer.Save(
                modelPath,
                network,
                new float[GraphNetwork.NodeInputSize],
                Enumerable.Repeat(1f, GraphNetwork.NodeInputSize).ToArray(),
                new float[GraphNetwork.OutputSize],
                Enumerable.Repeat(1f, GraphNetwork.OutputSize).ToArray(),
                overview.Sha256,
                $"epochs=1 minibatches={results.Count} samples={trainedSamples} "
                    + $"batchSize={batchSize} overflowSamplesExcluded={overview.OverflowSampleCount} "
                    + $"loss={meanLoss.ToString("0.000000", CultureInfo.InvariantCulture)}");
            stopwatch.Stop();
            overview = TrainingDashboard.AttachModel(overview, modelPath);
            TrainingDashboard.WriteReport(
                reportPath,
                overview,
                "Concluido",
                results,
                stopwatch.Elapsed,
                trainingSampleLimit: maxSamples);
            Console.WriteLine($"Modelo salvo: {modelPath}");
            Console.WriteLine($"Painel de treinamento: {reportPath}");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            stopwatch.Stop();
            TrainingDashboard.WriteReport(
                reportPath,
                overview,
                "Falhou",
                results,
                stopwatch.Elapsed,
                exception.Message,
                maxSamples);
            throw;
        }
    }
}
