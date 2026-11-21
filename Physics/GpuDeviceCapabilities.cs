using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal readonly record struct GpuDeviceCapabilities(
    long MaximumShaderStorageBlockSize,
    int MaximumComputeWorkGroupInvocations,
    int MaximumComputeWorkGroupSizeX)
{
    public static GpuDeviceCapabilities Capture()
    {
        GL.GetError();
        GL.GetInteger64((GetPName)0x90DE, out var maximumSsboSize);
        GL.GetInteger(GetPName.MaxComputeWorkGroupInvocations, out var maximumInvocations);
        // OpenGL drivers expose the workgroup size as a vector-valued pname.
        // It is redundant for this shader because invocations already enforces
        // the local 128-thread limit; retain the invocation value for diagnostics.
        var maximumWorkGroupSizeX = maximumInvocations;
        var glError = GL.GetError();
        if (maximumSsboSize <= 0
            || maximumInvocations <= 0
            || maximumWorkGroupSizeX <= 0)
        {
            throw new NotSupportedException(
                "O driver nao informou limites validos para compute shaders e SSBOs "
                + $"(SSBO={maximumSsboSize:N0}, invocations={maximumInvocations:N0}, "
                + $"workgroupX={maximumWorkGroupSizeX:N0}, GL error={glError}).");
        }
        return new GpuDeviceCapabilities(
            maximumSsboSize,
            maximumInvocations,
            maximumWorkGroupSizeX);
    }

    public void ValidateSimulation(int diskCount)
    {
        if (MaximumComputeWorkGroupInvocations < SimulationSettings.WorkGroupSize)
        {
            throw new NotSupportedException(
                $"O limite de invocacoes por workgroup e "
                + $"{MaximumComputeWorkGroupInvocations:N0}; "
                + $"o simulador requer {SimulationSettings.WorkGroupSize:N0}.");
        }
        if (MaximumComputeWorkGroupSizeX < SimulationSettings.WorkGroupSize)
        {
            throw new NotSupportedException(
                $"O limite X de workgroups e {MaximumComputeWorkGroupSizeX:N0}; "
                + $"o simulador requer {SimulationSettings.WorkGroupSize:N0}.");
        }

        var requiredPositionBytes = checked((long)diskCount * 4 * sizeof(float));
        var requiredGridBytes = checked((long)SimulationSettings.GridBucketCount * sizeof(int));
        if (requiredPositionBytes > MaximumShaderStorageBlockSize
            || requiredGridBytes > MaximumShaderStorageBlockSize)
        {
            throw new NotSupportedException(
                $"O tamanho do SSBO excede o limite deste driver "
                + $"({MaximumShaderStorageBlockSize:N0} bytes).");
        }
    }
}
