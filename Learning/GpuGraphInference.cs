using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal sealed class GpuGraphInference : IDisposable
{
    private readonly GpuModelWeights _weights;
    private readonly int _encodeProgram;
    private readonly int _messageProgram;
    private readonly int _decodeProgram;
    private readonly int[] _latentBuffers = new int[2];
    private readonly int _nodeInputBuffer;
    private readonly int _edgeFeatureBuffer;
    private readonly int _edgeTargetBuffer;
    private readonly int _edgeOffsetBuffer;
    private readonly int _outputBuffer;
    private int _nodeCapacity;
    private bool _disposed;

    public GpuGraphInference(GpuModelWeights weights)
    {
        _weights = weights ?? throw new ArgumentNullException(nameof(weights));
        _encodeProgram = GlProgram.CreateCompute(ShaderSource.Load("Neural/EncodeNodes.comp"));
        _messageProgram = GlProgram.CreateCompute(ShaderSource.Load("Neural/MessagePassing.comp"));
        _decodeProgram = GlProgram.CreateCompute(ShaderSource.Load("Neural/DecodePhysics.comp"));
        _latentBuffers[0] = GL.GenBuffer();
        _latentBuffers[1] = GL.GenBuffer();
        _nodeInputBuffer = GL.GenBuffer();
        _edgeFeatureBuffer = GL.GenBuffer();
        _edgeTargetBuffer = GL.GenBuffer();
        _edgeOffsetBuffer = GL.GenBuffer();
        _outputBuffer = GL.GenBuffer();
    }

    public int OutputBuffer => _outputBuffer;

    public float[] Forward(
        float[] nodeInputs,
        float[] edgeFeatures,
        int[] edgeCounts,
        int[] edgeTargets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nodeInputs);
        ArgumentNullException.ThrowIfNull(edgeFeatures);
        ArgumentNullException.ThrowIfNull(edgeCounts);
        ArgumentNullException.ThrowIfNull(edgeTargets);

        var nodeCount = edgeCounts.Length;
        if (nodeCount == 0
            || nodeInputs.Length != checked(nodeCount * GraphNetwork.NodeInputSize))
        {
            throw new ArgumentException("Dimensoes invalidas para entradas de nos.");
        }
        if (edgeFeatures.Length != checked(edgeTargets.Length * GraphNetwork.EdgeInputSize)
            || edgeCounts.Any(count => count < 0)
            || edgeCounts.Sum() != edgeTargets.Length)
        {
            throw new ArgumentException("CSR de arestas invalido.");
        }
        if (edgeTargets.Any(target => target < 0 || target >= nodeCount))
        {
            throw new ArgumentException("Indice de destino de aresta invalido.");
        }
        if (nodeInputs.Any(value => !float.IsFinite(value))
            || edgeFeatures.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Entradas GNN contem NaN ou Infinity.");
        }

        var edgeOffsets = new uint[nodeCount + 1];
        for (var node = 0; node < nodeCount; node++)
        {
            edgeOffsets[node + 1] = checked(edgeOffsets[node] + (uint)edgeCounts[node]);
        }

        Upload(_nodeInputBuffer, nodeInputs);
        Upload(_edgeFeatureBuffer, edgeFeatures);
        Upload(_edgeTargetBuffer, edgeTargets.Select(value => (uint)value).ToArray());
        Upload(_edgeOffsetBuffer, edgeOffsets);
        DispatchFromBuffers(
            nodeCount,
            _nodeInputBuffer,
            _edgeFeatureBuffer,
            _edgeTargetBuffer,
            _edgeOffsetBuffer);
        return ReadOutput(nodeCount);
    }

    public void DispatchFromBuffers(
        int nodeCount,
        int nodeInputBuffer,
        int edgeFeatureBuffer,
        int edgeTargetBuffer,
        int edgeOffsetBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (nodeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount));
        }
        if (nodeInputBuffer <= 0
            || edgeFeatureBuffer <= 0
            || edgeTargetBuffer <= 0
            || edgeOffsetBuffer <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeInputBuffer), "SSBO handles devem ser validos.");
        }
        EnsureCapacity(nodeCount);

        var nodeEncoder = _weights.GetLayer("node_encoder");
        _weights.Bind(0);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, nodeInputBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, _latentBuffers[0]);
        GL.UseProgram(_encodeProgram);
        SetUniform(_encodeProgram, "uNodeCount", (uint)nodeCount);
        SetUniform(_encodeProgram, "uWeightOffset", (uint)nodeEncoder.WeightFloatOffset);
        SetUniform(_encodeProgram, "uBiasOffset", (uint)nodeEncoder.BiasFloatOffset);
        GL.DispatchCompute((nodeCount + 63) / 64, 1, 1);
        StorageBarrier();

        var latentInput = 0;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var message = _weights.GetLayer($"message_{iteration}");
            var update = _weights.GetLayer($"update_{iteration}");
            var edgeEncoder = _weights.GetLayer("edge_encoder");
            _weights.Bind(0);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, edgeFeatureBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, edgeTargetBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, edgeOffsetBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, _latentBuffers[latentInput]);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5, _latentBuffers[1 - latentInput]);
            GL.UseProgram(_messageProgram);
            SetUniform(_messageProgram, "uNodeCount", (uint)nodeCount);
            SetUniform(_messageProgram, "uEdgeWeightOffset", (uint)edgeEncoder.WeightFloatOffset);
            SetUniform(_messageProgram, "uEdgeBiasOffset", (uint)edgeEncoder.BiasFloatOffset);
            SetUniform(_messageProgram, "uMessageWeightOffset", (uint)message.WeightFloatOffset);
            SetUniform(_messageProgram, "uMessageBiasOffset", (uint)message.BiasFloatOffset);
            SetUniform(_messageProgram, "uUpdateWeightOffset", (uint)update.WeightFloatOffset);
            SetUniform(_messageProgram, "uUpdateBiasOffset", (uint)update.BiasFloatOffset);
            GL.DispatchCompute((nodeCount + 63) / 64, 1, 1);
            StorageBarrier();
            latentInput = 1 - latentInput;
        }

        var decoder = _weights.GetLayer("decoder");
        _weights.Bind(0);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _latentBuffers[latentInput]);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, _outputBuffer);
        GL.UseProgram(_decodeProgram);
        SetUniform(_decodeProgram, "uNodeCount", (uint)nodeCount);
        SetUniform(_decodeProgram, "uWeightOffset", (uint)decoder.WeightFloatOffset);
        SetUniform(_decodeProgram, "uBiasOffset", (uint)decoder.BiasFloatOffset);
        GL.DispatchCompute((nodeCount + 63) / 64, 1, 1);
        StorageBarrier();
    }

    public float[] ReadOutput(int nodeCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (nodeCount <= 0 || nodeCount > _nodeCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount));
        }
        var output = new float[nodeCount * GraphNetwork.OutputSize];
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _outputBuffer);
        GL.GetBufferSubData(
            BufferTarget.ShaderStorageBuffer,
            IntPtr.Zero,
            output.Length * sizeof(float),
            output);
        if (output.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Inferencia GPU produziu NaN ou Infinity.");
        }
        return output;
    }

    private void EnsureCapacity(int nodeCount)
    {
        if (nodeCount <= _nodeCapacity) return;
        _nodeCapacity = nodeCount;
        var latentBytes = checked(nodeCount * GraphNetwork.LatentSize * sizeof(float));
        var outputBytes = checked(nodeCount * GraphNetwork.OutputSize * sizeof(float));
        Allocate(_latentBuffers[0], latentBytes);
        Allocate(_latentBuffers[1], latentBytes);
        Allocate(_outputBuffer, outputBytes);
    }

    private static void Upload(int buffer, float[] values)
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            Math.Max(sizeof(float), checked(values.Length * sizeof(float))),
            values.Length == 0 ? new float[1] : values,
            BufferUsageHint.DynamicDraw);
    }

    private static void Upload(int buffer, uint[] values)
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            Math.Max(sizeof(uint), checked(values.Length * sizeof(uint))),
            values.Length == 0 ? new uint[1] : values,
            BufferUsageHint.DynamicDraw);
    }

    private static void Allocate(int buffer, int byteCount)
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            byteCount,
            IntPtr.Zero,
            BufferUsageHint.DynamicDraw);
    }

    private static void StorageBarrier()
    {
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
    }

    private static void SetUniform(int program, string name, uint value)
    {
        var location = GL.GetUniformLocation(program, name);
        if (location < 0)
        {
            throw new InvalidOperationException($"Uniform obrigatorio nao encontrado: {name}.");
        }
        GL.Uniform1(location, value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        GL.DeleteProgram(_encodeProgram);
        GL.DeleteProgram(_messageProgram);
        GL.DeleteProgram(_decodeProgram);
        GL.DeleteBuffer(_latentBuffers[0]);
        GL.DeleteBuffer(_latentBuffers[1]);
        GL.DeleteBuffer(_nodeInputBuffer);
        GL.DeleteBuffer(_edgeFeatureBuffer);
        GL.DeleteBuffer(_edgeTargetBuffer);
        GL.DeleteBuffer(_edgeOffsetBuffer);
        GL.DeleteBuffer(_outputBuffer);
        _disposed = true;
    }
}
