namespace NPhysics;

internal sealed class DatasetLoader
{
    public sealed record TrainingFrame(
        float[] NodeInput,
        float[] EdgeFlat,
        int[] EdgeCounts,
        int[] EdgeTargets,
        float[] Target);

    public static IEnumerable<TrainingFrame> Read(
        string filePath,
        int maxNodesPerBatch = 256,
        long maxSamples = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodesPerBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSamples);
        using var reader = new DatasetReader(filePath);
        long yieldedSamples = 0;
        foreach (var block in reader.ReadBlocks())
        {
            var sampleCount = block.Samples.Length / DatasetSchema.FloatsPerSample;
            var localIndices = new int[sampleCount];
            Array.Fill(localIndices, -1);
            var originalIndices = new int[maxNodesPerBatch];
            var cursor = 0;
            while (cursor < sampleCount && yieldedSamples < maxSamples)
            {
                var batchCount = 0;
                while (cursor < sampleCount && batchCount < maxNodesPerBatch)
                {
                    var originalIndex = cursor++;
                    var offset = originalIndex * DatasetSchema.FloatsPerSample;
                    if (block.Samples[offset + 376] > 0f)
                    {
                        continue;
                    }

                    originalIndices[batchCount] = originalIndex;
                    localIndices[originalIndex] = batchCount;
                    batchCount++;
                }

                if (batchCount == 0)
                {
                    continue;
                }

                var nodeInput = new float[batchCount * GraphNetwork.NodeInputSize];
                var target = new float[batchCount * GraphNetwork.OutputSize];
                var edgeCounts = new int[batchCount];
                var edgeTargets = new List<int>();
                var edgeFeatures = new List<float>();

                for (var localIndex = 0; localIndex < batchCount; localIndex++)
                {
                    var offset = originalIndices[localIndex] * DatasetSchema.FloatsPerSample;
                    CopyNodeInput(block.Samples, offset, nodeInput, localIndex * GraphNetwork.NodeInputSize);
                    Array.Copy(
                        block.Samples,
                        offset + 36,
                        target,
                        localIndex * GraphNetwork.OutputSize,
                        3);
                    Array.Copy(
                        block.Samples,
                        offset + 39,
                        target,
                        localIndex * GraphNetwork.OutputSize + 3,
                        3);
                    Array.Copy(
                        block.Samples,
                        offset + 42,
                        target,
                        localIndex * GraphNetwork.OutputSize + 6,
                        3);

                    var neighborCount = Math.Clamp(
                        (int)block.Samples[offset + 375],
                        0,
                        DatasetSchema.MaxNeighborCount);
                    for (var neighbor = 0; neighbor < neighborCount; neighbor++)
                    {
                        var neighborOffset = offset + 48 + neighbor * 20;
                        var originalTarget = (int)block.Samples[neighborOffset + 19];
                        if (originalTarget < 0
                            || originalTarget >= sampleCount
                            || localIndices[originalTarget] < 0)
                        {
                            continue;
                        }

                        for (var feature = 0; feature < GraphNetwork.EdgeInputSize - 1; feature++)
                        {
                            edgeFeatures.Add(block.Samples[neighborOffset + feature]);
                        }
                        edgeFeatures.Add(
                            block.Samples[offset + 3] + block.Samples[neighborOffset + 11]);
                        edgeTargets.Add(localIndices[originalTarget]);
                        edgeCounts[localIndex]++;
                    }
                }

                for (var localIndex = 0; localIndex < batchCount; localIndex++)
                {
                    localIndices[originalIndices[localIndex]] = -1;
                }

                yieldedSamples += batchCount;
                yield return new TrainingFrame(
                    nodeInput,
                    edgeFeatures.ToArray(),
                    edgeCounts,
                    edgeTargets.ToArray(),
                    target);
            }
        }
    }

    private static void CopyNodeInput(float[] sample, int source, float[] destination, int target)
    {
        Array.Copy(sample, source + 0, destination, target + 0, 3);
        destination[target + 3] = sample[source + 3];
        Array.Copy(sample, source + 4, destination, target + 4, 3);
        destination[target + 6] = sample[source + 7];
        Array.Copy(sample, source + 8, destination, target + 8, 4);
        Array.Copy(sample, source + 12, destination, target + 12, 3);
        destination[target + 15] = sample[source + 15];
        Array.Copy(sample, source + 16, destination, target + 16, 3);
        destination[target + 19] = sample[source + 19];
        Array.Copy(sample, source + 20, destination, target + 20, 3);
        destination[target + 23] = sample[source + 23];
        Array.Copy(sample, source + 27, destination, target + 24, 3);
        Array.Copy(sample, source + 368, destination, target + 27, 3);
        destination[target + 30] = sample[source + 371];
        destination[target + 31] = sample[source + 372];
        destination[target + 32] = sample[source + 373];
        destination[target + 33] = sample[source + 374];
        destination[target + 34] = sample[source + 45];
        destination[target + 35] = sample[source + 46];
        destination[target + 36] = sample[source + 47];
        destination[target + 37] = sample[source + 375];
        destination[target + 38] = sample[source + 376];
        destination[target + 39] = sample[source + 377];
        destination[target + 40] = sample[source + 378];
        destination[target + 41] = sample[source + 380];
    }
}
