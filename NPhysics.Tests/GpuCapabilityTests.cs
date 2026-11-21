namespace NPhysics.Tests;

public sealed class GpuCapabilityTests
{
    [Fact]
    public void RejectsUnsupportableWorkGroupSize()
    {
        var capabilities = new GpuDeviceCapabilities(
            MaximumShaderStorageBlockSize: 1_000_000_000,
            MaximumComputeWorkGroupInvocations: 64,
            MaximumComputeWorkGroupSizeX: 64);

        Assert.Throws<NotSupportedException>(
            () => capabilities.ValidateSimulation(4096));
    }

    [Fact]
    public void RejectsSsbOAboveDriverLimit()
    {
        var capabilities = new GpuDeviceCapabilities(
            MaximumShaderStorageBlockSize: 100,
            MaximumComputeWorkGroupInvocations: 1024,
            MaximumComputeWorkGroupSizeX: 1024);

        Assert.Throws<NotSupportedException>(
            () => capabilities.ValidateSimulation(4096));
    }
}
