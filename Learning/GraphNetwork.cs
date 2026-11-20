namespace NPhysics;

internal sealed class GraphNetwork
{
    public const int NodeInputSize = 48;
    public const int EdgeInputSize = 20;
    public const int LatentSize = 64;
    public const int OutputSize = 9;

    private readonly LinearLayer _nodeEncoder;
    private readonly LinearLayer _edgeEncoder;
    private readonly LinearLayer[] _messageLayers;
    private readonly LinearLayer[] _updateLayers;
    private readonly LinearLayer _decoder;
    private readonly float[][] _nodeStatesBefore = new float[2][];
    private readonly float[][] _aggregatedBefore = new float[2][];
    private readonly float[][] _updateInputs = new float[2][];
    private int[] _edgeCountsCache = [];
    private int[] _edgeTargetsCache = [];
    private float[] _edgeFlatCache = [];
    private float[] _nodeInputCache = [];
    private float[] _nodeStatesFinal = [];
    private readonly float[][][] _messageInputs = new float[2][][];
    private readonly float[][][] _edgeStates = new float[2][][];
    private readonly float[][][] _messageStates = new float[2][][];

    public GraphNetwork(int seed = 2026)
    {
        _nodeEncoder = new LinearLayer(NodeInputSize, LatentSize, seed);
        _edgeEncoder = new LinearLayer(EdgeInputSize, LatentSize, seed + 1);
        _messageLayers =
        [
            new LinearLayer(LatentSize * 3, LatentSize, seed + 2),
            new LinearLayer(LatentSize * 3, LatentSize, seed + 3),
        ];
        _updateLayers =
        [
            new LinearLayer(LatentSize * 2, LatentSize, seed + 4),
            new LinearLayer(LatentSize * 2, LatentSize, seed + 5),
        ];
        _decoder = new LinearLayer(LatentSize, OutputSize, seed + 6);
    }

    public float[] Forward(float[] nodeInput, float[] edgeFlat, int[] edgeCounts, int[] edgeTargets)
    {
        _nodeInputCache = nodeInput;
        _edgeFlatCache = edgeFlat;
        _edgeCountsCache = edgeCounts;
        _edgeTargetsCache = edgeTargets;
        var nodeCount = edgeCounts.Length;
        var nodeStates = new float[nodeCount * LatentSize];
        for (var node = 0; node < nodeCount; node++)
        {
            var input = new float[NodeInputSize];
            Array.Copy(nodeInput, node * NodeInputSize, input, 0, NodeInputSize);
            var encoded = Silu(_nodeEncoder.Forward(input));
            Array.Copy(encoded, 0, nodeStates, node * LatentSize, LatentSize);
        }

        for (var iteration = 0; iteration < 2; iteration++)
        {
            _nodeStatesBefore[iteration] = (float[])nodeStates.Clone();
            var aggregated = new float[nodeCount * LatentSize];
            var edgeOffset = 0;
            for (var source = 0; source < nodeCount; source++)
            {
                var edgeCount = edgeCounts[source];
                for (var edge = 0; edge < edgeCount; edge++)
                {
                    var target = edgeTargets[edgeOffset];
                    var edgeInput = new float[EdgeInputSize];
                    Array.Copy(edgeFlat, edgeOffset * EdgeInputSize, edgeInput, 0, EdgeInputSize);
                    var edgeState = Silu(_edgeEncoder.Forward(edgeInput));
                    var messageInput = Concatenate(
                        Slice(nodeStates, source),
                        Slice(nodeStates, target),
                        edgeState);
                    var message = Silu(_messageLayers[iteration].Forward(messageInput));
                    var aggregateOffset = source * LatentSize;
                    var scale = edgeCount > 0 ? 1f / MathF.Sqrt(edgeCount) : 0f;
                    for (var latent = 0; latent < LatentSize; latent++)
                    {
                        aggregated[aggregateOffset + latent] += message[latent] * scale;
                    }
                    edgeOffset++;
                }
            }
            _aggregatedBefore[iteration] = (float[])aggregated.Clone();
            _updateInputs[iteration] = new float[nodeCount * LatentSize * 2];

            for (var node = 0; node < nodeCount; node++)
            {
                var updateInput = Concatenate(
                    Slice(nodeStates, node),
                    Slice(aggregated, node));
                Array.Copy(updateInput, 0, _updateInputs[iteration], node * 128, 128);
                var update = Silu(_updateLayers[iteration].Forward(updateInput));
                var offset = node * LatentSize;
                for (var latent = 0; latent < LatentSize; latent++)
                {
                    nodeStates[offset + latent] = update[latent];
                }
            }
        }

        var output = new float[nodeCount * OutputSize];
        for (var node = 0; node < nodeCount; node++)
        {
            var decoded = _decoder.Forward(Slice(nodeStates, node));
            Array.Copy(decoded, 0, output, node * OutputSize, OutputSize);
        }
        _nodeStatesFinal = (float[])nodeStates.Clone();
        return output;
    }

    public float[] ForwardOne(float[] nodeInput)
    {
        var encoded = Silu(_nodeEncoder.Forward(nodeInput));
        return _decoder.Forward(encoded);
    }

    public void BackwardDecoder(float[] outputGradient, int nodeCount)
    {
        for (var node = 0; node < nodeCount; node++)
        {
            var gradient = new float[OutputSize];
            Array.Copy(
                outputGradient,
                node * OutputSize,
                gradient,
                0,
                OutputSize);
            var nodeState = Slice(_nodeStatesFinal, node);
            _decoder.Backward(nodeState, gradient);
        }
    }

    private static float[] Concatenate(params float[][] values)
    {
        var result = new float[values.Sum(value => value.Length)];
        var offset = 0;
        foreach (var value in values)
        {
            Array.Copy(value, 0, result, offset, value.Length);
            offset += value.Length;
        }
        return result;
    }

    private static float[] Slice(float[] values, int index)
    {
        var slice = new float[LatentSize];
        Array.Copy(values, index * LatentSize, slice, 0, LatentSize);
        return slice;
    }

    private static float[] Silu(float[] values)
    {
        var result = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = values[i] / (1f + MathF.Exp(-values[i]));
        }
        return result;
    }

    public IEnumerable<LinearLayer> Layers
    {
        get
        {
            yield return _nodeEncoder;
            yield return _edgeEncoder;
            foreach (var layer in _messageLayers) yield return layer;
            foreach (var layer in _updateLayers) yield return layer;
            yield return _decoder;
        }
    }
}
