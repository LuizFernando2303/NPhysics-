using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal sealed class GpuModelWeights : IDisposable
{
    private readonly Dictionary<string, GpuLayerSlice> _layers;
    private readonly float[] _packedWeights;
    private bool _disposed;

    public GpuModelWeights(ModelSerializer.ModelParameters model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _layers = new Dictionary<string, GpuLayerSlice>(StringComparer.Ordinal);
        var packed = new List<float>();
        foreach (var layer in model.Layers.OrderBy(layer => LayerOrder(layer.Name)))
        {
            var weightOffset = packed.Count;
            packed.AddRange(layer.Weights);
            var biasOffset = packed.Count;
            packed.AddRange(layer.Bias);
            _layers.Add(
                layer.Name,
                new GpuLayerSlice(
                    layer.Name,
                    layer.InputSize,
                    layer.OutputSize,
                    weightOffset,
                    biasOffset));
        }

        _packedWeights = packed.ToArray();
        if (_packedWeights.Length == 0)
        {
            throw new InvalidDataException("Modelo sem pesos para upload.");
        }
        if (_packedWeights.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Modelo possui pesos NaN ou Infinity.");
        }

        GL.GetInteger64((GetPName)0x90DE, out var maximumBlockSize);
        var requiredBytes = checked((long)_packedWeights.Length * sizeof(float));
        if (requiredBytes > maximumBlockSize)
        {
            throw new NotSupportedException(
                $"Modelo requer {requiredBytes} bytes de SSBO; limite deste driver: {maximumBlockSize}.");
        }

        BufferId = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, BufferId);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            checked(_packedWeights.Length * sizeof(float)),
            _packedWeights,
            BufferUsageHint.StaticDraw);
    }

    public int BufferId { get; }
    public int FloatCount => _packedWeights.Length;
    public int ByteSize => checked(_packedWeights.Length * sizeof(float));
    public IReadOnlyCollection<GpuLayerSlice> Layers => _layers.Values;

    public GpuLayerSlice GetLayer(string name)
    {
        return _layers.TryGetValue(name, out var layer)
            ? layer
            : throw new InvalidDataException($"Camada nao encontrada no modelo: {name}.");
    }

    public float[] GetPackedWeightsForValidation() => (float[])_packedWeights.Clone();

    public void Bind(int binding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, binding, BufferId);
    }

    private static int LayerOrder(string name) => name switch
    {
        "node_encoder" => 0,
        "edge_encoder" => 1,
        "message_0" => 2,
        "message_1" => 3,
        "update_0" => 4,
        "update_1" => 5,
        "decoder" => 6,
        _ => throw new InvalidDataException($"Camada desconhecida no modelo: {name}."),
    };

    public void Dispose()
    {
        if (_disposed) return;
        GL.DeleteBuffer(BufferId);
        _disposed = true;
    }
}

internal readonly record struct GpuLayerSlice(
    string Name,
    int InputSize,
    int OutputSize,
    int WeightFloatOffset,
    int BiasFloatOffset);
