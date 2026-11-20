using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal sealed class AsyncGpuReadback : IDisposable
{
    private readonly int[] _buffers;
    private readonly nint[] _fences;
    private readonly int[] _byteCounts;
    private readonly uint[] _sequences;
    private int _nextSlot;

    public AsyncGpuReadback(int ringSize = 3)
    {
        if (ringSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(ringSize));
        }
        _buffers = new int[ringSize];
        _fences = new nint[ringSize];
        _byteCounts = new int[ringSize];
        _sequences = new uint[ringSize];
        for (var i = 0; i < ringSize; i++)
        {
            _buffers[i] = GL.GenBuffer();
        }
    }

    public bool Schedule(int sourceBuffer, int byteCount, uint sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceBuffer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        var slot = -1;
        for (var attempt = 0; attempt < _buffers.Length; attempt++)
        {
            var candidate = (_nextSlot + attempt) % _buffers.Length;
            if (_fences[candidate] == 0)
            {
                slot = candidate;
                break;
            }
        }
        if (slot < 0)
        {
            return false;
        }

        _nextSlot = (slot + 1) % _buffers.Length;
        Resize(slot, byteCount);
        GL.BindBuffer(BufferTarget.CopyReadBuffer, sourceBuffer);
        GL.BindBuffer(BufferTarget.CopyWriteBuffer, _buffers[slot]);
        GL.CopyBufferSubData(
            BufferTarget.CopyReadBuffer,
            BufferTarget.CopyWriteBuffer,
            0,
            0,
            byteCount);
        GL.Flush();
        var fence = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, 0);
        if (fence == 0)
        {
            throw new InvalidOperationException("Nao foi possivel criar a fence do readback GPU.");
        }
        _fences[slot] = fence;
        _byteCounts[slot] = byteCount;
        _sequences[slot] = sequence;
        return true;
    }

    public bool TryRead(out float[] values, out uint sequence)
    {
        for (var attempt = 0; attempt < _buffers.Length; attempt++)
        {
            var slot = (_nextSlot + attempt) % _buffers.Length;
            if (_fences[slot] == 0)
            {
                continue;
            }
            var status = GL.ClientWaitSync(_fences[slot], ClientWaitSyncFlags.None, 0);
            if (status == WaitSyncStatus.TimeoutExpired)
            {
                continue;
            }
            if (status == WaitSyncStatus.WaitFailed)
            {
                GL.DeleteSync(_fences[slot]);
                _fences[slot] = 0;
                _byteCounts[slot] = 0;
                throw new InvalidOperationException("Falha ao consultar a fence do readback GPU.");
            }
            if (status is not (WaitSyncStatus.AlreadySignaled or WaitSyncStatus.ConditionSatisfied))
            {
                continue;
            }

            GL.DeleteSync(_fences[slot]);
            _fences[slot] = 0;
            var floatCount = _byteCounts[slot] / sizeof(float);
            values = new float[floatCount];
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _buffers[slot]);
            var ptr = GL.MapBufferRange(
                BufferTarget.ShaderStorageBuffer,
                IntPtr.Zero,
                _byteCounts[slot],
                MapBufferAccessMask.MapReadBit);
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Nao foi possivel mapear o staging buffer.");
            }
            Marshal.Copy(ptr, values, 0, values.Length);
            if (!GL.UnmapBuffer(BufferTarget.ShaderStorageBuffer))
            {
                _byteCounts[slot] = 0;
                throw new InvalidDataException("Staging buffer de readback foi corrompido durante o mapeamento.");
            }
            _byteCounts[slot] = 0;
            sequence = _sequences[slot];
            return true;
        }
        values = [];
        sequence = 0;
        return false;
    }

    private void Resize(int slot, int byteCount)
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _buffers[slot]);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            byteCount,
            IntPtr.Zero,
            BufferUsageHint.StreamRead);
    }

    public void Dispose()
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            if (_fences[i] != 0)
            {
                GL.DeleteSync(_fences[i]);
            }
            GL.DeleteBuffer(_buffers[i]);
        }
    }
}
