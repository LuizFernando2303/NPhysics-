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
    private readonly float[][] _updateInputs = new float[2][];
    private readonly float[][] _updatePreActivations = new float[2][];
    private readonly float[][] _messageInputs = new float[2][];
    private readonly float[][] _messagePreActivations = new float[2][];
    private readonly float[][] _edgePreActivations = new float[2][];
    private readonly float[][] _edgeStates = new float[2][];
    private float[] _nodeEncoderPreActivations = [];
    private float[] _nodeInputCache = [];
    private float[] _edgeInputCache = [];
    private int[] _edgeCountsCache = [];
    private int[] _edgeTargetsCache = [];
    private float[] _nodeStatesFinal = [];

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
        _edgeInputCache = edgeFlat;
        _edgeCountsCache = edgeCounts;
        _edgeTargetsCache = edgeTargets;
        var nodeCount = edgeCounts.Length;
        var nodeStates = new float[nodeCount * LatentSize];
        _nodeEncoderPreActivations = new float[nodeCount * LatentSize];
        for (var node = 0; node < nodeCount; node++)
        {
            var input = new float[NodeInputSize];
            Array.Copy(nodeInput, node * NodeInputSize, input, 0, NodeInputSize);
            var preActivation = _nodeEncoder.Forward(input);
            Array.Copy(preActivation, 0, _nodeEncoderPreActivations, node * LatentSize, LatentSize);
            var encoded = Silu(preActivation);
            Array.Copy(encoded, 0, nodeStates, node * LatentSize, LatentSize);
        }

        for (var iteration = 0; iteration < 2; iteration++)
        {
            _nodeStatesBefore[iteration] = (float[])nodeStates.Clone();
            var aggregated = new float[nodeCount * LatentSize];
            var edgeOffset = 0;
            _edgePreActivations[iteration] = new float[edgeTargets.Length * LatentSize];
            _edgeStates[iteration] = new float[edgeTargets.Length * LatentSize];
            _messageInputs[iteration] = new float[edgeTargets.Length * LatentSize * 3];
            _messagePreActivations[iteration] = new float[edgeTargets.Length * LatentSize];
            for (var source = 0; source < nodeCount; source++)
            {
                var edgeCount = edgeCounts[source];
                for (var edge = 0; edge < edgeCount; edge++)
                {
                    var target = edgeTargets[edgeOffset];
                    var edgeInput = new float[EdgeInputSize];
                    Array.Copy(edgeFlat, edgeOffset * EdgeInputSize, edgeInput, 0, EdgeInputSize);
                    var edgePreActivation = _edgeEncoder.Forward(edgeInput);
                    Array.Copy(edgePreActivation, 0, _edgePreActivations[iteration], edgeOffset * LatentSize, LatentSize);
                    var edgeState = Silu(edgePreActivation);
                    Array.Copy(edgeState, 0, _edgeStates[iteration], edgeOffset * LatentSize, LatentSize);
                    var messageInput = Concatenate(
                        Slice(nodeStates, source),
                        Slice(nodeStates, target),
                        edgeState);
                    Array.Copy(messageInput, 0, _messageInputs[iteration], edgeOffset * LatentSize * 3, LatentSize * 3);
                    var messagePreActivation = _messageLayers[iteration].Forward(messageInput);
                    Array.Copy(messagePreActivation, 0, _messagePreActivations[iteration], edgeOffset * LatentSize, LatentSize);
                    var message = Silu(messagePreActivation);
                    var aggregateOffset = source * LatentSize;
                    var scale = edgeCount > 0 ? 1f / MathF.Sqrt(edgeCount) : 0f;
                    for (var latent = 0; latent < LatentSize; latent++)
                    {
                        aggregated[aggregateOffset + latent] += message[latent] * scale;
                    }
                    edgeOffset++;
                }
            }
            _updateInputs[iteration] = new float[nodeCount * LatentSize * 2];
            _updatePreActivations[iteration] = new float[nodeCount * LatentSize];

            for (var node = 0; node < nodeCount; node++)
            {
                var updateInput = Concatenate(
                    Slice(nodeStates, node),
                    Slice(aggregated, node));
                Array.Copy(updateInput, 0, _updateInputs[iteration], node * 128, 128);
                var updatePreActivation = _updateLayers[iteration].Forward(updateInput);
                Array.Copy(updatePreActivation, 0, _updatePreActivations[iteration], node * LatentSize, LatentSize);
                var update = Silu(updatePreActivation);
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

    public void Backward(float[] outputGradient)
    {
        var nodeCount = _edgeCountsCache.Length;
        if (outputGradient.Length != nodeCount * OutputSize)
        {
            throw new ArgumentException("Gradiente de saida com dimensao invalida.");
        }
        var latentGradient = new float[nodeCount * LatentSize];
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
            var inputGradient = _decoder.Backward(nodeState, gradient);
            Array.Copy(inputGradient, 0, latentGradient, node * LatentSize, LatentSize);
        }

        for (var iteration = 1; iteration >= 0; iteration--)
        {
            var previousGradient = new float[nodeCount * LatentSize];
            var aggregateGradient = new float[nodeCount * LatentSize];
            for (var node = 0; node < nodeCount; node++)
            {
                var preActivationGradient = new float[LatentSize];
                for (var latent = 0; latent < LatentSize; latent++)
                {
                    var offset = node * LatentSize + latent;
                    preActivationGradient[latent] = latentGradient[offset]
                        * SiluDerivative(_updatePreActivations[iteration][offset]);
                }

                var updateInput = new float[LatentSize * 2];
                Array.Copy(_updateInputs[iteration], node * LatentSize * 2, updateInput, 0, updateInput.Length);
                var updateInputGradient = _updateLayers[iteration].Backward(updateInput, preActivationGradient);
                Array.Copy(updateInputGradient, 0, previousGradient, node * LatentSize, LatentSize);
                Array.Copy(
                    updateInputGradient,
                    LatentSize,
                    aggregateGradient,
                    node * LatentSize,
                    LatentSize);
            }

            var edgeOffset = 0;
            for (var source = 0; source < nodeCount; source++)
            {
                var edgeCount = _edgeCountsCache[source];
                var scale = edgeCount > 0 ? 1f / MathF.Sqrt(edgeCount) : 0f;
                for (var edge = 0; edge < edgeCount; edge++, edgeOffset++)
                {
                    var target = _edgeTargetsCache[edgeOffset];
                    var messageGradient = new float[LatentSize];
                    for (var latent = 0; latent < LatentSize; latent++)
                    {
                        var offset = edgeOffset * LatentSize + latent;
                        messageGradient[latent] = aggregateGradient[source * LatentSize + latent]
                            * scale
                            * SiluDerivative(_messagePreActivations[iteration][offset]);
                    }

                    var messageInput = new float[LatentSize * 3];
                    Array.Copy(_messageInputs[iteration], edgeOffset * LatentSize * 3, messageInput, 0, messageInput.Length);
                    var messageInputGradient = _messageLayers[iteration].Backward(messageInput, messageGradient);
                    for (var latent = 0; latent < LatentSize; latent++)
                    {
                        previousGradient[source * LatentSize + latent] += messageInputGradient[latent];
                        previousGradient[target * LatentSize + latent] += messageInputGradient[LatentSize + latent];
                    }

                    var edgeGradient = new float[LatentSize];
                    for (var latent = 0; latent < LatentSize; latent++)
                    {
                        var offset = edgeOffset * LatentSize + latent;
                        edgeGradient[latent] = messageInputGradient[2 * LatentSize + latent]
                            * SiluDerivative(_edgePreActivations[iteration][offset]);
                    }
                    var edgeInput = new float[EdgeInputSize];
                    Array.Copy(_edgeInputCache, edgeOffset * EdgeInputSize, edgeInput, 0, EdgeInputSize);
                    _edgeEncoder.Backward(edgeInput, edgeGradient);
                }
            }
            latentGradient = previousGradient;
        }

        for (var node = 0; node < nodeCount; node++)
        {
            var nodeGradient = new float[LatentSize];
            for (var latent = 0; latent < LatentSize; latent++)
            {
                var offset = node * LatentSize + latent;
                nodeGradient[latent] = latentGradient[offset]
                    * SiluDerivative(_nodeEncoderPreActivations[offset]);
            }
            var nodeInput = new float[NodeInputSize];
            Array.Copy(_nodeInputCache, node * NodeInputSize, nodeInput, 0, NodeInputSize);
            _nodeEncoder.Backward(nodeInput, nodeGradient);
        }
    }

    public void LoadParameters(ModelSerializer.ModelParameters model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var savedLayers = model.Layers.ToDictionary(layer => layer.Name, StringComparer.Ordinal);
        foreach (var (name, layer) in NamedLayers)
        {
            if (!savedLayers.TryGetValue(name, out var saved)
                || saved.InputSize != layer.InputSize
                || saved.OutputSize != layer.OutputSize
                || saved.Weights.Length != layer.Weight.Length
                || saved.Bias.Length != layer.Bias.Length)
            {
                throw new InvalidDataException($"Pesos ausentes ou incompativeis para {name}.");
            }
            Array.Copy(saved.Weights, layer.Weight, layer.Weight.Length);
            Array.Copy(saved.Bias, layer.Bias, layer.Bias.Length);
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

    private static float SiluDerivative(float value)
    {
        var sigmoid = 1f / (1f + MathF.Exp(-value));
        return sigmoid * (1f + value * (1f - sigmoid));
    }

    public IEnumerable<LinearLayer> Layers => NamedLayers.Select(layer => layer.Layer);

    public IEnumerable<(string Name, LinearLayer Layer)> NamedLayers
    {
        get
        {
            yield return ("node_encoder", _nodeEncoder);
            yield return ("edge_encoder", _edgeEncoder);
            yield return ("message_0", _messageLayers[0]);
            yield return ("message_1", _messageLayers[1]);
            yield return ("update_0", _updateLayers[0]);
            yield return ("update_1", _updateLayers[1]);
            yield return ("decoder", _decoder);
        }
    }
}
