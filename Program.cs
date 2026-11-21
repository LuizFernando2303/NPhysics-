namespace NPhysics;

internal static class Program
{
    private static void Main()
    {
        TrainingSmokeTests.Run();
        SimulationHudSmokeTests.Run();
        DatasetCaptureSmokeTests.Run();
        SimulationClockSmokeTests.Run();
        RigidBodyNumericsSmokeTests.Run();
        var commandLineArguments = Environment.GetCommandLineArgs();
        var experiment = ExperimentConfiguration.Create();
        string? neuralModelPath = null;
        int? maximumPhysicsSteps = null;
        string? experimentSavePath = null;
        if (commandLineArguments.Length == 2
            && commandLineArguments[1] == "--self-test")
        {
            Console.WriteLine("Smoke tests aprovados.");
            return;
        }
        for (var i = 1; i < commandLineArguments.Length - 1; i++)
        {
            if (commandLineArguments[i] == "--experiment")
            {
                experiment = ExperimentConfiguration.Load(commandLineArguments[i + 1]);
                experiment.Apply();
                neuralModelPath = experiment.NeuralModelPath;
                maximumPhysicsSteps = experiment.MaximumPhysicsSteps;
                i++;
            }
        }
        for (var i = 0; i < commandLineArguments.Length - 1; i++)
        {
            var argument = commandLineArguments[i];
            var value = commandLineArguments[i + 1];
            switch (argument)
            {
                case "--count":
                    if (int.TryParse(value, out var count) && count > 0)
                    {
                        SimulationSettings.DiskCount = count;
                        experiment = experiment with { DiskCount = count };
                    }
                    i++;
                    break;
                case "--seed":
                    if (int.TryParse(value, out var seed))
                    {
                        SimulationSettings.Seed = seed;
                        experiment = experiment with { Seed = seed };
                    }
                    i++;
                    break;
                case "--capture":
                    SimulationSettings.DatasetCaptureEnabled =
                        bool.TryParse(value, out var enabled) ? enabled : true;
                    experiment = experiment with
                    {
                        DatasetCaptureEnabled = SimulationSettings.DatasetCaptureEnabled,
                    };
                    i++;
                    break;
                case "--steps":
                    if (int.TryParse(value, out var steps) && steps > 0)
                    {
                        maximumPhysicsSteps = steps;
                        experiment = experiment with { MaximumPhysicsSteps = steps };
                    }
                    i++;
                    break;
                case "--train":
                    TrainingCommand.Run(value);
                    return;
                case "--model":
                    neuralModelPath = Path.GetFullPath(value);
                    if (!File.Exists(neuralModelPath))
                    {
                        throw new FileNotFoundException(
                            "Modelo neural nao encontrado.",
                            neuralModelPath);
                    }
                    experiment = experiment with { NeuralModelPath = neuralModelPath };
                    i++;
                    break;
                case "--experiment":
                    i++;
                    break;
                case "--save-experiment":
                    experimentSavePath = value;
                    i++;
                    break;
                case "--dataset-report":
                    var reportPath = TrainingDashboard.WriteDatasetReport(value);
                    Console.WriteLine($"Relatorio do dataset salvo em {reportPath}");
                    return;
                case "--validate-dataset":
                    long sampleTotal = 0;
                    long blockTotal = 0;
                    long overflowTotal = 0;
                    using (var datasetReader = new DatasetReader(value))
                    {
                        foreach (var block in datasetReader.ReadBlocks())
                        {
                            sampleTotal += block.Samples.Length / DatasetSchema.FloatsPerSample;
                            overflowTotal += block.OverflowCount;
                            blockTotal++;
                        }
                    }
                    Console.WriteLine(
                        $"Dataset valido: blocos={blockTotal:N0}, amostras={sampleTotal:N0}, "
                        + $"overflows={overflowTotal:N0}");
                    return;
                case "--gpu-model-check":
                    using (var probe = new GpuModelProbeWindow(value))
                    {
                        probe.Run();
                    }
                    return;
            }
        }
        if (experimentSavePath is not null)
        {
            experiment = experiment with
            {
                DiskCount = SimulationSettings.DiskCount,
                Seed = SimulationSettings.Seed,
                DatasetCaptureEnabled = SimulationSettings.DatasetCaptureEnabled,
                NeuralModelPath = neuralModelPath,
                MaximumPhysicsSteps = maximumPhysicsSteps,
            };
            experiment.Save(experimentSavePath);
            Console.WriteLine($"Configuracao de experimento salva: {Path.GetFullPath(experimentSavePath)}");
        }
        using var simulation = new PhysicsSimulation(neuralModelPath, maximumPhysicsSteps);
        simulation.Run();
    }

}
