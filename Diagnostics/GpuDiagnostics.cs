using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal sealed class GpuDiagnostics : IDisposable
{
    private const int MetricCount = 9;
    private const int MetricBufferSize = MetricCount * sizeof(uint);
    private const int ReadbackSlotCount = 4;
    private const int TimerQuerySlotCount = 4;
    public const int MaximumDiskCountForDetailedCounters = 4_096;
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromSeconds(1);

    private readonly ReadbackSlot[] _readbackSlots = new ReadbackSlot[ReadbackSlotCount];
    private readonly GpuTimerPool _gridClearTimers = new(TimerQuerySlotCount);
    private readonly GpuTimerPool _gridBuildTimers = new(TimerQuerySlotCount);
    private readonly GpuTimerPool _simulationTimers = new(TimerQuerySlotCount);
    private readonly GpuTimerPool _cullingTimers = new(TimerQuerySlotCount);
    private readonly GpuTimerPool _drawingTimers = new(TimerQuerySlotCount);
    private readonly uint[] _zeroMetrics = new uint[MetricCount];
    private readonly bool _detailedCountersAvailable;
    private long _lastCaptureTimestamp = Stopwatch.GetTimestamp();
    private long _captureId;
    private int _nextReadbackSlot;
    private bool _enabled = true;
    private bool _disposed;

    public GpuDiagnostics(int diskCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCount);
        _detailedCountersAvailable = diskCount <= MaximumDiskCountForDetailedCounters;
        MetricsBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, MetricsBuffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            MetricBufferSize,
            _zeroMetrics,
            BufferUsageHint.DynamicDraw);

        for (var index = 0; index < _readbackSlots.Length; index++)
        {
            var buffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.CopyWriteBuffer, buffer);
            GL.BufferData(
                BufferTarget.CopyWriteBuffer,
                MetricBufferSize,
                IntPtr.Zero,
                BufferUsageHint.StreamRead);
            _readbackSlots[index] = new ReadbackSlot(buffer);
        }
    }

    public int MetricsBuffer { get; }
    public bool Enabled => _enabled;
    public bool DetailedCountersAvailable => _detailedCountersAvailable;
    public bool DetailedCountersEnabled => _enabled && _detailedCountersAvailable;

    public void ResetWindow()
    {
        _lastCaptureTimestamp = Stopwatch.GetTimestamp();
        if (_enabled)
        {
            ClearMetrics();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        _lastCaptureTimestamp = Stopwatch.GetTimestamp();
        if (enabled)
        {
            ClearMetrics();
        }
    }

    public void BeginGridClearTiming()
    {
        if (_enabled)
        {
            _gridClearTimers.Begin();
        }
    }

    public void EndGridClearTiming()
    {
        if (_enabled)
        {
            _gridClearTimers.End();
        }
    }

    public void BeginGridBuildTiming()
    {
        if (_enabled)
        {
            _gridBuildTimers.Begin();
        }
    }

    public void EndGridBuildTiming()
    {
        if (_enabled)
        {
            _gridBuildTimers.End();
        }
    }

    public void BeginSimulationTiming()
    {
        if (_enabled)
        {
            _simulationTimers.Begin();
        }
    }

    public void EndSimulationTiming()
    {
        if (_enabled)
        {
            _simulationTimers.End();
        }
    }

    public void BeginCullingTiming()
    {
        if (_enabled)
        {
            _cullingTimers.Begin();
        }
    }

    public void EndCullingTiming()
    {
        if (_enabled)
        {
            _cullingTimers.End();
        }
    }

    public void BeginDrawingTiming()
    {
        if (_enabled)
        {
            _drawingTimers.Begin();
        }
    }

    public void EndDrawingTiming()
    {
        if (_enabled)
        {
            _drawingTimers.End();
        }
    }

    public bool TryScheduleSnapshot(TelemetryFrameWindow frameWindow)
    {
        if (!_enabled)
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();
        var intervalSeconds = Stopwatch.GetElapsedTime(_lastCaptureTimestamp, now).TotalSeconds;
        if (intervalSeconds < CaptureInterval.TotalSeconds)
        {
            return false;
        }

        var slotIndex = FindAvailableReadbackSlot();
        if (slotIndex < 0)
        {
            return false;
        }

        GL.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit);
        GL.BindBuffer(BufferTarget.CopyReadBuffer, MetricsBuffer);
        GL.BindBuffer(BufferTarget.CopyWriteBuffer, _readbackSlots[slotIndex].Buffer);
        GL.CopyBufferSubData(
            BufferTarget.CopyReadBuffer,
            BufferTarget.CopyWriteBuffer,
            IntPtr.Zero,
            IntPtr.Zero,
            MetricBufferSize);

        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, MetricsBuffer);
        GL.BufferSubData(
            BufferTarget.ShaderStorageBuffer,
            IntPtr.Zero,
            MetricBufferSize,
            _zeroMetrics);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        var slot = _readbackSlots[slotIndex];
        slot.Fence = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        if (slot.Fence == IntPtr.Zero)
        {
            throw new InvalidOperationException("Nao foi possivel criar a fence do readback de diagnosticos.");
        }

        slot.CaptureId = ++_captureId;
        slot.CapturedAtUtc = DateTime.UtcNow;
        slot.IntervalSeconds = intervalSeconds;
        slot.FrameWindow = frameWindow;
        slot.Pending = true;
        _nextReadbackSlot = (slotIndex + 1) % _readbackSlots.Length;
        _lastCaptureTimestamp = now;
        return true;
    }

    public bool TryReadSnapshot(out GpuTelemetrySample sample)
    {
        PollTimers();
        for (var offset = 0; offset < _readbackSlots.Length; offset++)
        {
            var slot = _readbackSlots[(_nextReadbackSlot + offset) % _readbackSlots.Length];
            if (!slot.Pending)
            {
                continue;
            }

            var result = GL.ClientWaitSync(slot.Fence, ClientWaitSyncFlags.None, 0);
            if (result == WaitSyncStatus.WaitFailed)
            {
                throw new InvalidOperationException("Falha ao consultar a fence do readback de diagnosticos.");
            }

            if (result is not (WaitSyncStatus.AlreadySignaled or WaitSyncStatus.ConditionSatisfied))
            {
                continue;
            }

            var values = new uint[MetricCount];
            GL.BindBuffer(BufferTarget.CopyReadBuffer, slot.Buffer);
            GL.GetBufferSubData(
                BufferTarget.CopyReadBuffer,
                IntPtr.Zero,
                MetricBufferSize,
                values);
            GL.DeleteSync(slot.Fence);
            slot.Fence = IntPtr.Zero;
            slot.Pending = false;

            sample = new GpuTelemetrySample(
                slot.CaptureId,
                slot.CapturedAtUtc,
                slot.IntervalSeconds,
                slot.FrameWindow,
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                BitConverter.UInt32BitsToSingle(values[6]),
                BitConverter.UInt32BitsToSingle(values[8]),
                values[7],
                _gridClearTimers.LastMilliseconds,
                _gridBuildTimers.LastMilliseconds,
                _simulationTimers.LastMilliseconds,
                _cullingTimers.LastMilliseconds,
                _drawingTimers.LastMilliseconds);
            return true;
        }

        sample = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var slot in _readbackSlots)
        {
            if (slot.Pending)
            {
                GL.DeleteSync(slot.Fence);
            }

            GL.DeleteBuffer(slot.Buffer);
        }

        GL.DeleteBuffer(MetricsBuffer);
        _gridClearTimers.Dispose();
        _gridBuildTimers.Dispose();
        _simulationTimers.Dispose();
        _cullingTimers.Dispose();
        _drawingTimers.Dispose();
        _disposed = true;
    }

    private void ClearMetrics()
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, MetricsBuffer);
        GL.BufferSubData(
            BufferTarget.ShaderStorageBuffer,
            IntPtr.Zero,
            MetricBufferSize,
            _zeroMetrics);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
    }

    private int FindAvailableReadbackSlot()
    {
        for (var offset = 0; offset < _readbackSlots.Length; offset++)
        {
            var index = (_nextReadbackSlot + offset) % _readbackSlots.Length;
            if (!_readbackSlots[index].Pending)
            {
                return index;
            }
        }

        return -1;
    }

    private void PollTimers()
    {
        _gridClearTimers.Poll();
        _gridBuildTimers.Poll();
        _simulationTimers.Poll();
        _cullingTimers.Poll();
        _drawingTimers.Poll();
    }

    private sealed class ReadbackSlot(int buffer)
    {
        public int Buffer { get; } = buffer;
        public IntPtr Fence { get; set; }
        public long CaptureId { get; set; }
        public DateTime CapturedAtUtc { get; set; }
        public double IntervalSeconds { get; set; }
        public TelemetryFrameWindow FrameWindow { get; set; }
        public bool Pending { get; set; }
    }

    private sealed class GpuTimerPool : IDisposable
    {
        private readonly int[] _queries = new int[TimerQuerySlotCount];
        private readonly bool[] _pending = new bool[TimerQuerySlotCount];
        private readonly long[] _sequences = new long[TimerQuerySlotCount];
        private int _activeIndex = -1;
        private int _nextIndex;
        private long _nextSequence;
        private long _lastCompletedSequence;

        public GpuTimerPool(int capacity)
        {
            if (capacity != _queries.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            GL.GenQueries(_queries.Length, _queries);
        }

        public double LastMilliseconds { get; private set; }

        public void Begin()
        {
            if (_activeIndex >= 0)
            {
                return;
            }

            Poll();
            for (var offset = 0; offset < _queries.Length; offset++)
            {
                var index = (_nextIndex + offset) % _queries.Length;
                if (_pending[index])
                {
                    continue;
                }

                _activeIndex = index;
                _nextIndex = (index + 1) % _queries.Length;
                GL.BeginQuery(QueryTarget.TimeElapsed, _queries[index]);
                return;
            }
        }

        public void End()
        {
            if (_activeIndex < 0)
            {
                return;
            }

            GL.EndQuery(QueryTarget.TimeElapsed);
            _pending[_activeIndex] = true;
            _sequences[_activeIndex] = ++_nextSequence;
            _activeIndex = -1;
        }

        public void Poll()
        {
            for (var index = 0; index < _queries.Length; index++)
            {
                if (!_pending[index])
                {
                    continue;
                }

                GL.GetQueryObject(
                    _queries[index],
                    GetQueryObjectParam.QueryResultAvailable,
                    out int available);
                if (available == 0)
                {
                    continue;
                }

                GL.GetQueryObject(
                    _queries[index],
                    GetQueryObjectParam.QueryResult,
                    out long nanoseconds);
                if (_sequences[index] > _lastCompletedSequence)
                {
                    LastMilliseconds = nanoseconds / 1_000_000.0;
                    _lastCompletedSequence = _sequences[index];
                }

                _pending[index] = false;
            }
        }

        public void Dispose()
        {
            if (_activeIndex >= 0)
            {
                GL.EndQuery(QueryTarget.TimeElapsed);
                _activeIndex = -1;
            }

            GL.DeleteQueries(_queries.Length, _queries);
        }
    }
}
