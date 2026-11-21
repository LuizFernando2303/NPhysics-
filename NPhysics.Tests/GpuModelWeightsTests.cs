namespace NPhysics.Tests;

public sealed class GpuModelWeightsTests
{
    [Fact]
    public void RejectsMissingOrMalformedLayer()
    {
        var model = CreateModel();
        model.Layers[0] = model.Layers[0] with
        {
            Weights = new float[1],
        };

        Assert.Throws<InvalidDataException>(
            () => GpuModelWeights.ValidateModelArchitecture(model));
    }

    [Fact]
    public void AcceptsAllExpectedGpuLayers()
    {
        var model = CreateModel();

        GpuModelWeights.ValidateModelArchitecture(model);
    }

    private static ModelSerializer.ModelParameters CreateModel()
    {
        var layers = new[]
        {
            CreateLayer("node_encoder", GraphNetwork.NodeInputSize, GraphNetwork.LatentSize),
            CreateLayer("edge_encoder", GraphNetwork.EdgeInputSize, GraphNetwork.LatentSize),
            CreateLayer("message_0", GraphNetwork.LatentSize * 3, GraphNetwork.LatentSize),
            CreateLayer("message_1", GraphNetwork.LatentSize * 3, GraphNetwork.LatentSize),
            CreateLayer("update_0", GraphNetwork.LatentSize * 2, GraphNetwork.LatentSize),
            CreateLayer("update_1", GraphNetwork.LatentSize * 2, GraphNetwork.LatentSize),
            CreateLayer("decoder", GraphNetwork.LatentSize, GraphNetwork.OutputSize),
        };
        return new ModelSerializer.ModelParameters(
            GraphNetwork.NodeInputSize,
            GraphNetwork.EdgeInputSize,
            GraphNetwork.LatentSize,
            GraphNetwork.OutputSize,
            new float[GraphNetwork.NodeInputSize],
            Enumerable.Repeat(1f, GraphNetwork.NodeInputSize).ToArray(),
            new float[GraphNetwork.OutputSize],
            Enumerable.Repeat(1f, GraphNetwork.OutputSize).ToArray(),
            new string('0', 64),
            "test",
            layers);
    }

    private static ModelSerializer.LayerParameters CreateLayer(
        string name,
        int inputSize,
        int outputSize)
    {
        return new ModelSerializer.LayerParameters(
            name,
            inputSize,
            outputSize,
            new float[checked(inputSize * outputSize)],
            new float[outputSize]);
    }
}
