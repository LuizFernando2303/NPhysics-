using System.Security.Cryptography;
using System.Text;

namespace NPhysics;

internal sealed class ModelSerializer
{
    public const uint ModelMagic = 0x4D50484Eu;
    public const ushort ModelVersion = 2;
    private const int HashByteCount = 64;
    private const int HistoryByteCount = 256;
    private const int LayerNameByteCount = 32;

    public sealed record LayerParameters(
        string Name,
        int InputSize,
        int OutputSize,
        float[] Weights,
        float[] Bias);

    public sealed record ModelParameters(
        int NodeInputSize,
        int EdgeInputSize,
        int LatentSize,
        int OutputSize,
        float[] NodeMeans,
        float[] NodeStandardDeviations,
        float[] TargetMeans,
        float[] TargetStandardDeviations,
        string DatasetHash,
        string TrainingHistory,
        LayerParameters[] Layers);

    public static void Save(
        string filePath,
        GraphNetwork network,
        float[] nodeMeans,
        float[] nodeStandardDeviations,
        float[] targetMeans,
        float[] targetStandardDeviations,
        string datasetHash,
        string trainingHistory)
    {
        ValidateNormalization(nodeMeans, nodeStandardDeviations, GraphNetwork.NodeInputSize);
        ValidateNormalization(targetMeans, targetStandardDeviations, GraphNetwork.OutputSize);
        var layers = network.NamedLayers.ToArray();

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(ModelMagic);
        writer.Write(ModelVersion);
        writer.Write(DatasetSchema.SchemaVersion);
        writer.Write(GraphNetwork.NodeInputSize);
        writer.Write(GraphNetwork.EdgeInputSize);
        writer.Write(GraphNetwork.LatentSize);
        writer.Write(GraphNetwork.OutputSize);
        WriteArray(writer, nodeMeans);
        WriteArray(writer, nodeStandardDeviations);
        WriteArray(writer, targetMeans);
        WriteArray(writer, targetStandardDeviations);
        WriteFixedString(writer, datasetHash, HashByteCount);
        WriteFixedString(writer, trainingHistory, HistoryByteCount);
        writer.Write(layers.Length);
        foreach (var (name, layer) in layers)
        {
            WriteFixedString(writer, name, LayerNameByteCount);
            writer.Write(layer.InputSize);
            writer.Write(layer.OutputSize);
            WriteArray(writer, layer.Weight);
            WriteArray(writer, layer.Bias);
        }
    }

    public static ModelParameters Load(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadUInt32() != ModelMagic)
        {
            throw new InvalidDataException("Arquivo .nphysmodel invalido: assinatura desconhecida.");
        }
        var version = reader.ReadUInt16();
        if (version != ModelVersion)
        {
            throw new InvalidDataException(
                $"Versao de modelo incompatível: {version}; esperada {ModelVersion}.");
        }
        var datasetSchemaVersion = reader.ReadUInt16();
        if (datasetSchemaVersion != DatasetSchema.SchemaVersion)
        {
            throw new InvalidDataException(
                $"Schema do dataset incompatível: {datasetSchemaVersion}.");
        }

        var nodeInputSize = reader.ReadInt32();
        var edgeInputSize = reader.ReadInt32();
        var latentSize = reader.ReadInt32();
        var outputSize = reader.ReadInt32();
        if (nodeInputSize != GraphNetwork.NodeInputSize
            || edgeInputSize != GraphNetwork.EdgeInputSize
            || latentSize != GraphNetwork.LatentSize
            || outputSize != GraphNetwork.OutputSize)
        {
            throw new InvalidDataException("Dimensoes da arquitetura do modelo nao sao suportadas.");
        }

        var nodeMeans = ReadArray(reader, GraphNetwork.NodeInputSize);
        var nodeStandardDeviations = ReadArray(reader, GraphNetwork.NodeInputSize);
        var targetMeans = ReadArray(reader, GraphNetwork.OutputSize);
        var targetStandardDeviations = ReadArray(reader, GraphNetwork.OutputSize);
        ValidateNormalization(nodeMeans, nodeStandardDeviations, GraphNetwork.NodeInputSize);
        ValidateNormalization(targetMeans, targetStandardDeviations, GraphNetwork.OutputSize);
        var datasetHash = ReadFixedString(reader, HashByteCount);
        var history = ReadFixedString(reader, HistoryByteCount);

        var layerCount = reader.ReadInt32();
        var expectedLayers = new Dictionary<string, (int Input, int Output)>(StringComparer.Ordinal)
        {
            ["node_encoder"] = (GraphNetwork.NodeInputSize, GraphNetwork.LatentSize),
            ["edge_encoder"] = (GraphNetwork.EdgeInputSize, GraphNetwork.LatentSize),
            ["message_0"] = (GraphNetwork.LatentSize * 3, GraphNetwork.LatentSize),
            ["message_1"] = (GraphNetwork.LatentSize * 3, GraphNetwork.LatentSize),
            ["update_0"] = (GraphNetwork.LatentSize * 2, GraphNetwork.LatentSize),
            ["update_1"] = (GraphNetwork.LatentSize * 2, GraphNetwork.LatentSize),
            ["decoder"] = (GraphNetwork.LatentSize, GraphNetwork.OutputSize),
        };
        if (layerCount != expectedLayers.Count)
        {
            throw new InvalidDataException("Quantidade de camadas invalida no modelo.");
        }

        var layers = new LayerParameters[layerCount];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < layerCount; i++)
        {
            var name = ReadFixedString(reader, LayerNameByteCount);
            var inputSize = reader.ReadInt32();
            var outputSizeLayer = reader.ReadInt32();
            if (!expectedLayers.TryGetValue(name, out var dimensions)
                || !seen.Add(name)
                || dimensions.Input != inputSize
                || dimensions.Output != outputSizeLayer)
            {
                throw new InvalidDataException($"Camada ausente, duplicada ou incompativel: {name}.");
            }
            var weights = ReadArray(reader, checked(inputSize * outputSizeLayer));
            var bias = ReadArray(reader, outputSizeLayer);
            layers[i] = new LayerParameters(name, inputSize, outputSizeLayer, weights, bias);
        }
        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("O modelo contem dados extras ou possui tamanho invalido.");
        }

        return new ModelParameters(
            nodeInputSize,
            edgeInputSize,
            latentSize,
            outputSize,
            nodeMeans,
            nodeStandardDeviations,
            targetMeans,
            targetStandardDeviations,
            datasetHash,
            history,
            layers);
    }

    public static string HashFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteArray(BinaryWriter writer, float[] values)
    {
        writer.Write(values.Length);
        foreach (var value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException("Modelo contem NaN ou Infinity.");
            }
            writer.Write(value);
        }
    }

    private static float[] ReadArray(BinaryReader reader, int expectedLength)
    {
        var length = reader.ReadInt32();
        if (length != expectedLength)
        {
            throw new InvalidDataException("Tamanho de tensor inconsistente no modelo.");
        }
        var values = new float[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = reader.ReadSingle();
            if (!float.IsFinite(values[i]))
            {
                throw new InvalidDataException("Modelo contem NaN ou Infinity.");
            }
        }
        return values;
    }

    private static void ValidateNormalization(float[] means, float[] deviations, int expectedLength)
    {
        if (means.Length != expectedLength || deviations.Length != expectedLength)
        {
            throw new ArgumentException("Dimensao das estatisticas de normalizacao invalida.");
        }
        if (means.Any(value => !float.IsFinite(value))
            || deviations.Any(value => !float.IsFinite(value) || value <= 0f))
        {
            throw new InvalidDataException("Estatisticas de normalizacao invalidas.");
        }
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteCount)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > byteCount)
        {
            throw new ArgumentException($"String excede o limite de {byteCount} bytes.");
        }
        writer.Write(bytes);
        writer.Write(new byte[byteCount - bytes.Length]);
    }

    private static string ReadFixedString(BinaryReader reader, int byteCount)
    {
        var bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
        {
            throw new EndOfStreamException("Arquivo de modelo truncado.");
        }
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
