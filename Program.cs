namespace NPhysics;

internal static class Program
{
    private static void Main()
    {
        TrainingSmokeTests.Run();
        for (var i = 0; i < Environment.GetCommandLineArgs().Length - 1; i++)
        {
            var argument = Environment.GetCommandLineArgs()[i];
            var value = Environment.GetCommandLineArgs()[i + 1];
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
                    var network = new GraphNetwork();
                    var trainer = new GraphTrainer(
                        network,
                        new AdamOptimizer(1e-3f));
                    var frameCount = 0;
                    var lossSum = 0d;
                    foreach (var frame in DatasetLoader.Read(value))
                    {
                        lossSum += trainer.TrainSample(
                            frame.NodeInput,
                            frame.EdgeFlat,
                            frame.EdgeCounts,
                            frame.EdgeTargets,
                            frame.Target);
                        frameCount++;
                    }
                    if (frameCount == 0)
                    {
                        throw new InvalidDataException("O dataset nao contem amostras.");
                    }
                    var modelPath = Path.ChangeExtension(value, ".nphysmodel");
                    ModelSerializer.Save(
                        modelPath,
                        network,
                        new float[GraphNetwork.NodeInputSize],
                        Enumerable.Repeat(1f, GraphNetwork.NodeInputSize).ToArray(),
                        new float[GraphNetwork.OutputSize],
                        Enumerable.Repeat(1f, GraphNetwork.OutputSize).ToArray(),
                        ModelSerializer.HashFile(value),
                        $"epochs=1 blocks={frameCount} loss={lossSum / frameCount:0.000000}");
                    Console.WriteLine(
                        $"Modelo salvo em {modelPath}; loss medio={(lossSum / frameCount):0.000000}");
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
