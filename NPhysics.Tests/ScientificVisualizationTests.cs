namespace NPhysics.Tests;

public sealed class ScientificVisualizationTests
{
    [Fact]
    public void VisualizationCycleReturnsToFirstMode()
    {
        var physics = new GpuPhysics();
        var renderer = new SceneRenderer(new ProceduralTrack(), physics);

        Assert.Equal(ScientificVisualizationMode.PotentialEnergy, renderer.VisualizationMode);
        for (var i = 0; i < Enum.GetValues<ScientificVisualizationMode>().Length; i++)
        {
            renderer.CycleVisualizationMode();
        }
        Assert.Equal(ScientificVisualizationMode.PotentialEnergy, renderer.VisualizationMode);
        Assert.NotEmpty(renderer.VisualizationLabel);
    }
}
