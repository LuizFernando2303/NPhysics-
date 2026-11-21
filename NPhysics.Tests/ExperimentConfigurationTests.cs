namespace NPhysics.Tests;

public sealed class ExperimentConfigurationTests
{
    [Fact]
    public void SavesLoadsAndValidatesExperiment()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nphysics-{Guid.NewGuid():N}.json");
        try
        {
            var configuration = new ExperimentConfiguration
            {
                SchemaVersion = ExperimentConfiguration.CurrentVersion,
                DiskCount = 4096,
                Seed = 12345,
                DatasetCaptureEnabled = false,
                MaximumPhysicsSteps = 120,
            };
            configuration.Save(path);
            var loaded = ExperimentConfiguration.Load(path);

            Assert.Equal(4096, loaded.DiskCount);
            Assert.Equal(12345, loaded.Seed);
            Assert.False(loaded.DatasetCaptureEnabled);
            Assert.Equal(120, loaded.MaximumPhysicsSteps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsUnsupportedSchema()
    {
        var configuration = new ExperimentConfiguration
        {
            SchemaVersion = ExperimentConfiguration.CurrentVersion + 1,
        };

        Assert.Throws<InvalidDataException>(() => configuration.Save(
            Path.Combine(Path.GetTempPath(), $"nphysics-invalid-{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public void RejectsNonPositiveCount()
    {
        var configuration = new ExperimentConfiguration
        {
            DiskCount = 0,
        };

        Assert.Throws<InvalidDataException>(() => configuration.Save(
            Path.Combine(Path.GetTempPath(), $"nphysics-invalid-{Guid.NewGuid():N}.json")));
    }
}
