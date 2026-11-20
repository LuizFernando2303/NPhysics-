using System.Reflection;
using System.Globalization;

namespace NPhysics;

internal static class ShaderSource
{
    public static string Load(string fileName)
    {
        var resourceName = $"NPhysics.Shaders.{fileName}.glsl";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Shader embutido nao encontrado: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace("@CELL_SIZE@", Format(SimulationSettings.GridCellSize), StringComparison.Ordinal)
            .Replace(
                "@GRID_MASK@",
                "0x" + (SimulationSettings.GridBucketCount - 1).ToString("X8", CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace("@GRAVITY@", Format(SimulationSettings.Gravity), StringComparison.Ordinal)
            .Replace("@AIR_DENSITY@", Format(SimulationSettings.AirDensity), StringComparison.Ordinal)
            .Replace("@AIR_DRAG_COEFFICIENT@", Format(SimulationSettings.AirDragCoefficient), StringComparison.Ordinal)
            .Replace("@SPHERE_INERTIA_FACTOR@", Format(SimulationSettings.SolidSphereInertiaFactor), StringComparison.Ordinal)
            .Replace("@RAMP_HALF_WIDTH@", Format(SimulationSettings.RampHalfWidth), StringComparison.Ordinal)
            .Replace("@RAMP_SLOPE@", Format(SimulationSettings.RampSlope), StringComparison.Ordinal);
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
