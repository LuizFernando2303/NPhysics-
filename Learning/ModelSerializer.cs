using System.Security.Cryptography;
using System.Text;

namespace NPhysics;

internal sealed class ModelSerializer
{
    public const uint ModelMagic = 0x4D50484Eu;
    public const ushort ModelVersion = 1;

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
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(ModelMagic);
        writer.Write(ModelVersion);
        writer.Write(DatasetSchema.SchemaVersion);
        writer.Write(GraphNetwork.NodeInputSize);
        writer.Write(GraphNetwork.EdgeInputSize);
        writer.Write(GraphNetwork.LatentSize);
        writer.Write(GraphNetwork.OutputSize);
        writer.Write(nodeMeans.Length);
        foreach (var value in nodeMeans) writer.Write(value);
        writer.Write(nodeStandardDeviations.Length);
        foreach (var value in nodeStandardDeviations) writer.Write(value);
        writer.Write(targetMeans.Length);
        foreach (var value in targetMeans) writer.Write(value);
        writer.Write(targetStandardDeviations.Length);
        foreach (var value in targetStandardDeviations) writer.Write(value);
        WriteFixedString(writer, datasetHash, 64);
        WriteFixedString(writer, trainingHistory, 256);

        var layers = network.Layers.ToArray();
        writer.Write(layers.Length);
        foreach (var layer in layers)
        {
            writer.Write(layer.InputSize);
            writer.Write(layer.OutputSize);
            foreach (var value in layer.Weight) writer.Write(value);
            foreach (var value in layer.Bias) writer.Write(value);
        }
        writer.Flush();
    }

    public static string HashFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void WriteFixedString(BinaryWriter writer, string value, int byteCount)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes, 0, Math.Min(bytes.Length, byteCount));
        if (bytes.Length < byteCount)
        {
            writer.Write(new byte[byteCount - bytes.Length]);
        }
    }
}
