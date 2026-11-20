namespace NPhysics;

internal sealed class DatasetLoader
{
    public sealed record TrainingFrame(
        float[] NodeInput,
        float[] EdgeFlat,
        int[] EdgeCounts,
        int[] EdgeTargets,
        float[] Target);

    public static IEnumerable<TrainingFrame> Read(string filePath)
    {
        using var reader = new DatasetReader(filePath);
        foreach (var block in reader.ReadBlocks())
        {
            var sampleCount = block.Samples.Length / DatasetSchema.FloatsPerSample;
            var nodeInput = new float[sampleCount * GraphNetwork.NodeInputSize];
            var target = new float[sampleCount * GraphNetwork.OutputSize];
            var edgeCounts = new int[sampleCount];
            var edges = new List<int>();
            var edgeFeatures = new List<float>();

            for (var sample = 0; sample < sampleCount; sample++)
            {
                var offset = sample * DatasetSchema.FloatsPerSample;
                var nodeOffset = sample * GraphNetwork.NodeInputSize;
                Array.Copy(block.Samples, offset + 0, nodeInput, nodeOffset + 0, 3);
                nodeInput[nodeOffset + 3] = block.Samples[offset + 3];
                Array.Copy(block.Samples, offset + 4, nodeInput, nodeOffset + 4, 3);
                nodeInput[nodeOffset + 6] = block.Samples[offset + 7];
                Array.Copy(block.Samples, offset + 8, nodeInput, nodeOffset + 8, 4);
                Array.Copy(block.Samples, offset + 12, nodeInput, nodeOffset + 12, 3);
                nodeInput[nodeOffset + 15] = block.Samples[offset + 15];
                Array.Copy(block.Samples, offset + 16, nodeInput, nodeOffset + 16, 3);
                nodeInput[nodeOffset + 19] = block.Samples[offset + 19];
                Array.Copy(block.Samples, offset + 20, nodeInput, nodeOffset + 20, 3);
                nodeInput[nodeOffset + 23] = block.Samples[offset + 23];
                Array.Copy(block.Samples, offset + 27, nodeInput, nodeOffset + 24, 3);
                Array.Copy(block.Samples, offset + 368, nodeInput, nodeOffset + 27, 3);
                nodeInput[nodeOffset + 30] = block.Samples[offset + 371];
                nodeInput[nodeOffset + 31] = block.Samples[offset + 372];
                nodeInput[nodeOffset + 32] = block.Samples[offset + 373];
                nodeInput[nodeOffset + 33] = block.Samples[offset + 374];
                nodeInput[nodeOffset + 34] = block.Samples[offset + 45];
                nodeInput[nodeOffset + 35] = block.Samples[offset + 46];
                nodeInput[nodeOffset + 36] = block.Samples[offset + 47];
                nodeInput[nodeOffset + 37] = block.Samples[offset + 375];
                nodeInput[nodeOffset + 38] = block.Samples[offset + 376];
                nodeInput[nodeOffset + 39] = block.Samples[offset + 377];
                nodeInput[nodeOffset + 40] = block.Samples[offset + 378];
                nodeInput[nodeOffset + 41] = block.Samples[offset + 380];
                var targetOffset = sample * GraphNetwork.OutputSize;
                Array.Copy(block.Samples, offset + 36, target, targetOffset, 3);
                Array.Copy(block.Samples, offset + 39, target, targetOffset + 3, 3);
                Array.Copy(block.Samples, offset + 42, target, targetOffset + 6, 3);
                var edgeCount = 0;
                for (var neighbor = 0; neighbor < 16; neighbor++)
                {
                    if (neighbor >= block.Samples[offset + 375])
                    {
                        break;
                    }
                    var neighborOffset = offset + 48 + neighbor * 20;
                    for (var i = 0; i < 20; i++)
                    {
                        edgeFeatures.Add(block.Samples[neighborOffset + i]);
                    }
                    var neighborId = (int)block.Samples[neighborOffset + 19];
                    if (neighborId < 0 || neighborId >= sampleCount)
                    {
                        throw new InvalidDataException("Indice de vizinho invalido no dataset.");
                    }
                    edges.Add(neighborId);
                    edgeCount++;
                }
                edgeCounts[sample] = edgeCount;
            }
            yield return new TrainingFrame(
                nodeInput,
                edgeFeatures.ToArray(),
                edgeCounts,
                edges.ToArray(),
                target);
        }
    }
}
