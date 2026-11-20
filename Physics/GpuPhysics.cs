using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace NPhysics;

internal sealed class GpuPhysics : IDisposable
{
    private const int WorkGroupSize = SimulationSettings.WorkGroupSize;
    private const int GridBucketCount = SimulationSettings.GridBucketCount;
    private const int GridCellCount = GridBucketCount;
    private const int ObstacleBufferCapacity = 256;
    private const int IndirectCommandSize = 5 * sizeof(uint);

    private readonly int[] _positionBuffers = new int[2];
    private readonly int[] _velocityBuffers = new int[2];
    private readonly int[] _spinBuffers = new int[2];
    private int _currentState;
    private int _materialBuffer;
    private int _gridBuffer;
    private int _linksBuffer;
    private int _obstacleBuffer;
    private int _visibleDrawBuffer;
    private int _clearProgram;
    private int _buildProgram;
    private int _simulateProgram;
    private int _cullProgram;
    private int _clearCountLocation;
    private int _buildDiskCountLocation;
    private int _simulateDiskCountLocation;
    private int _obstacleCountLocation;
    private int _deltaTimeLocation;
    private int _cullDiskCountLocation;
    private int _viewProjectionLocation;
    private int _buildMetricsEnabledLocation;
    private int _simulateMetricsEnabledLocation;
    private int _cullMetricsEnabledLocation;
    private int _obstacleStartSegment = int.MinValue;
    private int _obstacleEndSegment = int.MinValue;
    private Obstacle[] _obstacles = [];
    private GpuDiagnostics? _diagnostics;

    public int PositionBuffer => _positionBuffers[_currentState];
    public int MaterialBuffer => _materialBuffer;
    public int SpinBuffer => _spinBuffers[_currentState];
    public int VisibleDrawBuffer => _visibleDrawBuffer;

    public void Initialize(ProceduralTrack track, Random random)
    {
        _clearProgram = GlProgram.CreateCompute(ShaderSource.Load("ClearGrid.comp"));
        _buildProgram = GlProgram.CreateCompute(ShaderSource.Load("BuildGrid.comp"));
        _simulateProgram = GlProgram.CreateCompute(ShaderSource.Load("Simulate.comp"));
        _cullProgram = GlProgram.CreateCompute(ShaderSource.Load("CullDisks.comp"));
        _diagnostics = new GpuDiagnostics();

        _positionBuffers[0] = GL.GenBuffer();
        _positionBuffers[1] = GL.GenBuffer();
        _velocityBuffers[0] = GL.GenBuffer();
        _velocityBuffers[1] = GL.GenBuffer();
        _spinBuffers[0] = GL.GenBuffer();
        _spinBuffers[1] = GL.GenBuffer();
        var spinBufferSize = SimulationSettings.DiskCount * 8 * sizeof(float);
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _spinBuffers[0]);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            spinBufferSize,
            IntPtr.Zero,
            BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _spinBuffers[1]);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            spinBufferSize,
            IntPtr.Zero,
            BufferUsageHint.DynamicDraw);
        _materialBuffer = CreateStorageBuffer(SimulationSettings.DiskCount * 4 * sizeof(float));
        _gridBuffer = CreateStorageBuffer(GridCellCount * sizeof(int));
        _linksBuffer = CreateStorageBuffer(SimulationSettings.DiskCount * sizeof(int));
        _obstacleBuffer = CreateStorageBuffer(ObstacleBufferCapacity * 8 * sizeof(float));
        _visibleDrawBuffer = CreateStorageBuffer(
            IndirectCommandSize + SimulationSettings.DiskCount * sizeof(uint));

        _clearCountLocation = GL.GetUniformLocation(_clearProgram, "uCellCount");
        _buildDiskCountLocation = GL.GetUniformLocation(_buildProgram, "uDiskCount");
        _simulateDiskCountLocation = GL.GetUniformLocation(_simulateProgram, "uDiskCount");
        _obstacleCountLocation = GL.GetUniformLocation(_simulateProgram, "uObstacleCount");
        _deltaTimeLocation = GL.GetUniformLocation(_simulateProgram, "uDeltaTime");
        _cullDiskCountLocation = GL.GetUniformLocation(_cullProgram, "uDiskCount");
        _viewProjectionLocation = GL.GetUniformLocation(_cullProgram, "uViewProjection");
        _buildMetricsEnabledLocation = GL.GetUniformLocation(_buildProgram, "uMetricsEnabled");
        _simulateMetricsEnabledLocation = GL.GetUniformLocation(_simulateProgram, "uMetricsEnabled");
        _cullMetricsEnabledLocation = GL.GetUniformLocation(_cullProgram, "uMetricsEnabled");

        Reset(track, random);
        UpdateObstacles(track, -8f, force: true);
    }

    public void Reset(ProceduralTrack track, Random random)
    {
        _diagnostics?.ResetWindow();
        var diskCount = SimulationSettings.DiskCount;
        var positions = new float[diskCount * 4];
        var velocities = new float[diskCount * 4];
        var spinStates = new float[diskCount * 8];
        var materials = new float[diskCount * 4];
        var lanesPerLayer = SimulationSettings.LaneCount;
        var disksPerXColumn = lanesPerLayer * SimulationSettings.VerticalLayerCount;
        var zStart = -((lanesPerLayer - 1) * SimulationSettings.SpawnSpacing) * 0.5f;

        for (var i = 0; i < diskCount; i++)
        {
            var radius = RandomRange(random, SimulationSettings.MinimumRadius, SimulationSettings.MaximumRadius);
            var xColumn = i / disksPerXColumn;
            var laneAndLayer = i % disksPerXColumn;
            var lane = laneAndLayer % lanesPerLayer;
            var layer = laneAndLayer / lanesPerLayer;
            var x = -8f + xColumn * SimulationSettings.SpawnSpacing;
            var z = zStart + lane * SimulationSettings.SpawnSpacing;
            var offset = i * 4;
            var surfacePoint = new Vector3(x, track.Height(x, z), z);
            var normal = track.SurfaceNormal(x, z);
            var surfaceOffset = radius + layer * SimulationSettings.VerticalLayerSpacing;
            var center = surfacePoint + normal * surfaceOffset;

            positions[offset] = center.X;
            positions[offset + 1] = center.Y;
            positions[offset + 2] = center.Z;
            positions[offset + 3] = radius;
            spinStates[i * 8 + 3] = 1f;

            materials[offset] = RandomRange(random, SimulationSettings.MinimumMass, SimulationSettings.MaximumMass);
            materials[offset + 1] = RandomRange(
                random,
                SimulationSettings.MinimumStaticFriction,
                SimulationSettings.MaximumStaticFriction);
            materials[offset + 2] = RandomRange(
                random,
                SimulationSettings.MinimumDynamicFriction,
                SimulationSettings.MaximumDynamicFriction);
            materials[offset + 3] = RandomRange(
                random,
                SimulationSettings.MinimumRestitution,
                SimulationSettings.MaximumRestitution);
        }

        UploadArray(_positionBuffers[0], positions);
        UploadArray(_positionBuffers[1], positions);
        UploadArray(_velocityBuffers[0], velocities);
        UploadArray(_velocityBuffers[1], velocities);
        UploadArray(_spinBuffers[0], spinStates);
        UploadArray(_spinBuffers[1], spinStates);
        UploadArray(_materialBuffer, materials);
        _currentState = 0;
    }

    public void Step(float deltaTime)
    {
        var outputState = 1 - _currentState;
        ClearGrid();
        BuildGrid();
        Simulate(outputState, deltaTime);
        _currentState = outputState;
    }

    public void CullVisibleDisks(Matrix4 viewProjection, int indexCount)
    {
        GL.BindBuffer(BufferTarget.DrawIndirectBuffer, _visibleDrawBuffer);
        uint[] command =
        [
            (uint)indexCount,
            0,
            0,
            0,
            0,
        ];
        GL.BufferSubData(BufferTarget.DrawIndirectBuffer, IntPtr.Zero, IndirectCommandSize, command);

        GL.UseProgram(_cullProgram);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, PositionBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _visibleDrawBuffer);
        GL.BindBufferBase(
            BufferRangeTarget.ShaderStorageBuffer,
            2,
            _diagnostics!.MetricsBuffer);
        GL.Uniform1(_cullDiskCountLocation, (uint)SimulationSettings.DiskCount);
        GL.Uniform1(_cullMetricsEnabledLocation, _diagnostics!.Enabled ? 1 : 0);
        GL.UniformMatrix4(_viewProjectionLocation, false, ref viewProjection);
        _diagnostics.BeginCullingTiming();
        GL.DispatchCompute((SimulationSettings.DiskCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
        _diagnostics.EndCullingTiming();
    }

    public Obstacle[] GetObstacles() => _obstacles;

    public void BeginDrawingTiming() => _diagnostics?.BeginDrawingTiming();

    public void EndDrawingTiming() => _diagnostics?.EndDrawingTiming();

    public void SetDiagnosticsEnabled(bool enabled)
    {
        _diagnostics?.SetEnabled(enabled);
    }

    public bool TryScheduleTelemetrySnapshot(TelemetryFrameWindow frameWindow)
    {
        return _diagnostics?.TryScheduleSnapshot(frameWindow) ?? false;
    }

    public bool TryReadTelemetry(out GpuTelemetrySample sample)
    {
        if (_diagnostics is not null)
        {
            return _diagnostics.TryReadSnapshot(out sample);
        }

        sample = default;
        return false;
    }

    public void Dispose()
    {
        _diagnostics?.Dispose();
        GL.DeleteBuffer(_positionBuffers[0]);
        GL.DeleteBuffer(_positionBuffers[1]);
        GL.DeleteBuffer(_velocityBuffers[0]);
        GL.DeleteBuffer(_velocityBuffers[1]);
        GL.DeleteBuffer(_spinBuffers[0]);
        GL.DeleteBuffer(_spinBuffers[1]);
        GL.DeleteBuffer(_materialBuffer);
        GL.DeleteBuffer(_gridBuffer);
        GL.DeleteBuffer(_linksBuffer);
        GL.DeleteBuffer(_obstacleBuffer);
        GL.DeleteBuffer(_visibleDrawBuffer);
        GL.DeleteProgram(_clearProgram);
        GL.DeleteProgram(_buildProgram);
        GL.DeleteProgram(_simulateProgram);
        GL.DeleteProgram(_cullProgram);
    }

    private void ClearGrid()
    {
        GL.UseProgram(_clearProgram);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, _gridBuffer);
        GL.Uniform1(_clearCountLocation, (uint)GridCellCount);
        _diagnostics!.BeginGridClearTiming();
        GL.DispatchCompute((GridCellCount + 255) / 256, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        _diagnostics.EndGridClearTiming();
    }

    private void BuildGrid()
    {
        GL.UseProgram(_buildProgram);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, PositionBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, _gridBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5, _linksBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6, _diagnostics!.MetricsBuffer);
        GL.Uniform1(_buildDiskCountLocation, (uint)SimulationSettings.DiskCount);
        GL.Uniform1(_buildMetricsEnabledLocation, _diagnostics.Enabled ? 1 : 0);
        _diagnostics.BeginGridBuildTiming();
        GL.DispatchCompute((SimulationSettings.DiskCount + WorkGroupSize - 1) / WorkGroupSize, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        _diagnostics.EndGridBuildTiming();
    }

    private void Simulate(int outputState, float deltaTime)
    {
        GL.UseProgram(_simulateProgram);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, PositionBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _velocityBuffers[_currentState]);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, _positionBuffers[outputState]);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, _velocityBuffers[outputState]);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, _gridBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5, _linksBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6, _obstacleBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7, _materialBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8, _diagnostics!.MetricsBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 9, _spinBuffers[_currentState]);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 10, _spinBuffers[outputState]);
        GL.Uniform1(_simulateDiskCountLocation, (uint)SimulationSettings.DiskCount);
        GL.Uniform1(_simulateMetricsEnabledLocation, _diagnostics.Enabled ? 1 : 0);
        GL.Uniform1(_obstacleCountLocation, _obstacles.Length);
        GL.Uniform1(_deltaTimeLocation, deltaTime);
        _diagnostics.BeginSimulationTiming();
        GL.DispatchCompute((SimulationSettings.DiskCount + WorkGroupSize - 1) / WorkGroupSize, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        _diagnostics.EndSimulationTiming();
    }

    public void UpdateObstacles(ProceduralTrack track, float cameraX, bool force = false)
    {
        var firstSegment = (int)MathF.Floor((cameraX - 52f) / ProceduralTrack.ObstacleSegmentLength);
        var lastSegment = (int)MathF.Ceiling((cameraX + 72f) / ProceduralTrack.ObstacleSegmentLength);
        if (!force && firstSegment == _obstacleStartSegment && lastSegment == _obstacleEndSegment)
        {
            return;
        }

        _obstacleStartSegment = firstSegment;
        _obstacleEndSegment = lastSegment;
        _obstacles = track.CreateObstacles(cameraX);
        if (_obstacles.Length > ObstacleBufferCapacity)
        {
            throw new InvalidOperationException("A quantidade de obstaculos excede a capacidade do buffer GPU.");
        }

        var data = new float[Math.Max(8, _obstacles.Length * 8)];
        for (var i = 0; i < _obstacles.Length; i++)
        {
            var obstacle = _obstacles[i];
            var offset = i * 8;
            data[offset] = obstacle.Position.X;
            data[offset + 1] = obstacle.Position.Y;
            data[offset + 2] = obstacle.Position.Z;
            data[offset + 3] = obstacle.Radius;
            data[offset + 4] = obstacle.HalfSize.X;
            data[offset + 5] = obstacle.HalfSize.Y;
            data[offset + 6] = obstacle.HalfSize.Z;
            data[offset + 7] = obstacle.IsBox ? 1f : 0f;
        }

        UploadArray(_obstacleBuffer, data);
    }

    private static int CreateStorageBuffer(int size)
    {
        var buffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
        GL.BufferData(BufferTarget.ShaderStorageBuffer, size, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        return buffer;
    }

    private static void UploadArray(int buffer, float[] values)
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, buffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            values.Length * sizeof(float),
            values,
            BufferUsageHint.DynamicDraw);
    }

    private static float RandomRange(Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }
}
