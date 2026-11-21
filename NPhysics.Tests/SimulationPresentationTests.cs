namespace NPhysics.Tests;

public sealed class SimulationPresentationTests
{
    [Fact]
    public void DefaultPresentationIsDashboard()
    {
        Assert.Equal(SimulationPresentation.Dashboard, default(SimulationPresentation));
    }

    [Theory]
    [InlineData("dashboard", 0)]
    [InlineData("DASHBOARD", 0)]
    [InlineData("scene", 1)]
    [InlineData("Scene", 1)]
    public void ParsesSupportedViews(string value, int expected)
    {
        Assert.Equal((SimulationPresentation)expected, SimulationPresentationParser.Parse(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("3d")]
    [InlineData("none")]
    public void RejectsUnsupportedViews(string value)
    {
        Assert.Throws<ArgumentException>(() => SimulationPresentationParser.Parse(value));
    }
}
