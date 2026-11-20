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
        for (var i = 0; i < nodeInput.Length; i++)
        {
            nodeInput[i] = MathF.Sin(i * 0.2f) * 0.4f;
        }
        for (var i = 0; i < edgeInput.Length; i++)
        {
            edgeInput[i] = MathF.Cos(i * 0.15f) * 0.3f;
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

        VerifyBackpropagation(network, nodeInput, edgeInput, edgeCounts, edgeTargets);

        var optimizer = new AdamOptimizer(1e-3f);
        var target = Enumerable.Repeat(0.1f, 18).ToArray();
        var weightCopies = network.NamedLayers
            .Select(item => (item.Name, Values: (float[])item.Layer.Weight.Clone()))
            .ToArray();
        var trainer = new GraphTrainer(network, optimizer);
        var loss = trainer.TrainSample(nodeInput, edgeInput, edgeCounts, edgeTargets, target);
        if (!float.IsFinite(loss))
        {
            throw new InvalidOperationException("O treino produziu uma loss invalida.");
        }
        foreach (var (name, previousWeights) in weightCopies)
        {
            var layer = network.NamedLayers.Single(item => item.Name == name).Layer;
            if (!previousWeights.Where((value, index) => value != layer.Weight[index]).Any())
            {
                throw new InvalidOperationException($"Backprop nao atualizou a camada {name}.");
            }
        }
    }

    private static void VerifyBackpropagation(
        GraphNetwork network,
        float[] nodeInput,
        float[] edgeInput,
        int[] edgeCounts,
        int[] edgeTargets)
    {
        var target = Enumerable.Repeat(0.1f, edgeCounts.Length * GraphNetwork.OutputSize).ToArray();
        var prediction = network.Forward(nodeInput, edgeInput, edgeCounts, edgeTargets);
        var outputGradient = new float[prediction.Length];
        for (var i = 0; i < prediction.Length; i++)
        {
            var difference = prediction[i] - target[i];
            var absolute = MathF.Abs(difference);
            outputGradient[i] = (absolute <= 0.1f
                ? difference
                : 0.1f * MathF.CopySign(1f, difference))
                / prediction.Length;
        }
        foreach (var layer in network.Layers)
        {
            layer.ZeroGradients();
        }
        network.Backward(outputGradient);

        const float epsilon = 0.01f;
        foreach (var layer in network.NamedLayers)
        {
            var weightIndex = 0;
            for (var i = 1; i < layer.Layer.WeightGradient.Length; i++)
            {
                if (MathF.Abs(layer.Layer.WeightGradient[i])
                    > MathF.Abs(layer.Layer.WeightGradient[weightIndex]))
                {
                    weightIndex = i;
                }
            }
            var analytic = layer.Layer.WeightGradient[weightIndex];
            var original = layer.Layer.Weight[weightIndex];
            layer.Layer.Weight[weightIndex] = original + epsilon;
            var positiveLoss = Loss(network.Forward(nodeInput, edgeInput, edgeCounts, edgeTargets), target);
            layer.Layer.Weight[weightIndex] = original - epsilon;
            var negativeLoss = Loss(network.Forward(nodeInput, edgeInput, edgeCounts, edgeTargets), target);
            layer.Layer.Weight[weightIndex] = original;
            var numeric = (positiveLoss - negativeLoss) / (2f * epsilon);
            var tolerance = 0.00001f + 0.05f * MathF.Abs(numeric);
            if (!float.IsFinite(analytic)
                || MathF.Abs(analytic - numeric) > tolerance)
            {
                throw new InvalidOperationException(
                    $"Gradiente inconsistente em {layer.Name}: analitico={analytic:G8}, "
                    + $"numerico={numeric:G8}.");
            }
        }
    }

    private static float Loss(float[] prediction, float[] target)
    {
        var gradient = new float[prediction.Length];
        return HuberLoss.ForwardBackward(prediction, target, gradient, 0.1f);
    }
}
