namespace NPhysics;

internal static class TrainingSmokeTests
{
    public static void Run()
    {
        var network = new GraphNetwork(seed: 123);
        var nodeInput = new float[GraphNetwork.NodeInputSize * 2];
        var edgeInput = new float[GraphNetwork.EdgeInputSize];
        var edgeCounts = new int[] { 1, 0 };
        var edgeTargets = new int[] { 1 };
        for (var i = 0; i < edgeInput.Length; i++)
        {
            edgeInput[i] = i * 0.01f;
        }
        var prediction = network.Forward(nodeInput, edgeInput, edgeCounts, edgeTargets);
        if (prediction.Length != 18)
        {
            throw new InvalidOperationException("Dimensao de saida da GNN invalida.");
        }
        if (prediction.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException("A GNN produziu NaN ou Infinity.");
        }

        var optimizer = new AdamOptimizer(1e-3f);
        var target = new float[18];
        var trainer = new GraphTrainer(network, optimizer);
        var loss = trainer.TrainSample(nodeInput, edgeInput, edgeCounts, edgeTargets, target);
        if (!float.IsFinite(loss))
        {
            throw new InvalidOperationException("O treino produziu uma loss invalida.");
        }
    }
}
