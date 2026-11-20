namespace NPhysics;

internal static class Program
{
    private static void Main()
    {
        TrainingSmokeTests.Run();
        SimulationHudSmokeTests.Run();
        DatasetCaptureSmokeTests.Run();
        var commandLineArguments = Environment.GetCommandLineArgs();
        if (commandLineArguments.Length == 2 && commandLineArguments[1] == "--self-test")
        {
            Console.WriteLine("Smoke tests aprovados.");
            return;
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
                    }
                    break;
                case "--seed":
                    if (int.TryParse(value, out var seed))
                    {
                        SimulationSettings.Seed = seed;
                    }
                    break;
                case "--capture":
                    SimulationSettings.DatasetCaptureEnabled =
                        bool.TryParse(value, out var enabled) ? enabled : true;
                    i++;
                    break;
                case "--train":
                    TrainingCommand.Run(value);
                    return;
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
        using var simulation = new PhysicsSimulation();
        simulation.Run();
    }

}
