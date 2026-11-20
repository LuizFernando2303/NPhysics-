namespace NPhysics;

internal sealed class GraphTrainer
{
    private readonly GraphNetwork _network;
    private readonly AdamOptimizer _optimizer;

    public GraphTrainer(GraphNetwork network, AdamOptimizer optimizer)
    {
        _network = network;
        _optimizer = optimizer;
    }

    public float TrainSample(float[] nodeInput, float[] edgeFlat, int[] edgeCounts, int[] edgeTargets, float[] target)
    {
        var nodeCount = edgeCounts.Length;
        var prediction = _network.Forward(nodeInput, edgeFlat, edgeCounts, edgeTargets);
        var gradient = new float[prediction.Length];
        var loss = 0f;
        for (var node = 0; node < nodeCount; node++)
        {
            var predictedNode = new float[GraphNetwork.OutputSize];
            var targetNode = new float[GraphNetwork.OutputSize];
            Array.Copy(prediction, node * GraphNetwork.OutputSize, predictedNode, 0, GraphNetwork.OutputSize);
            Array.Copy(target, node * GraphNetwork.OutputSize, targetNode, 0, GraphNetwork.OutputSize);
            loss += HuberLoss.ForwardBackward(
                predictedNode,
                targetNode,
                predictedNode,
                0.1f);
        }

        foreach (var layer in _network.Layers)
        {
            layer.ZeroGradients();
        }

        for (var node = 0; node < nodeCount; node++)
        {
            var targetNode = new float[GraphNetwork.OutputSize];
            Array.Copy(target, node * GraphNetwork.OutputSize, targetNode, 0, GraphNetwork.OutputSize);
            var predictedNode = new float[GraphNetwork.OutputSize];
            Array.Copy(prediction, node * GraphNetwork.OutputSize, predictedNode, 0, GraphNetwork.OutputSize);
            var nodeGradient = new float[GraphNetwork.OutputSize];
            HuberLoss.ForwardBackward(predictedNode, targetNode, nodeGradient, 0.1f);
            Array.Copy(
                nodeGradient,
                0,
                gradient,
                node * GraphNetwork.OutputSize,
                GraphNetwork.OutputSize);
        }
        _network.BackwardDecoder(gradient, nodeCount);
        _optimizer.Step(_network.Layers);
        return loss / Math.Max(1, nodeCount);
    }
}
