namespace NPhysics;

internal enum SimulationPresentation
{
    Dashboard,
    Scene,
}

internal static class SimulationPresentationParser
{
    public static SimulationPresentation Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.ToLowerInvariant() switch
        {
            "dashboard" => SimulationPresentation.Dashboard,
            "scene" => SimulationPresentation.Scene,
            _ => throw new ArgumentException(
                $"Unknown view '{value}'. Expected 'dashboard' or 'scene'.",
                nameof(value)),
        };
    }
}
