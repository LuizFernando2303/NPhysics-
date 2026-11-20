using System.Security.Cryptography;
using System.Text;
using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal sealed class DatasetGenerator : IDisposable
{
    private readonly DatasetCaptureSettings _settings;
    private readonly AsyncGpuReadback _captureReadback;
    private readonly int _captureBuffer;
    private readonly int _metadataBuffer;
    private readonly int _captureProgram;
    private readonly int _diskCountLocation;
    private readonly int _neighborLocation;
    private readonly int _deltaTimeLocation;
    private readonly DatasetWriter _writer;
    private int _captureCounter;
    private uint _frameIndex;
    private bool _enabled;

    public DatasetGenerator(DatasetCaptureSettings settings)
    {
        _settings = settings;
        _captureProgram = GlProgram.CreateCompute(
            ShaderSource.Load("Dataset/CaptureTraining.comp"));
        _captureBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _captureBuffer);
        var captureBytes = settings.DiskCount
            * DatasetSchema.FloatsPerSample
            * sizeof(float);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            captureBytes + 16 * sizeof(uint),
            IntPtr.Zero,
            BufferUsageHint.DynamicRead);

        _metadataBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _metadataBuffer);
        GL.BufferData(
            BufferTarget.ShaderStorageBuffer,
            16 * sizeof(uint),
            IntPtr.Zero,
            BufferUsageHint.DynamicRead);
        _captureReadback = new AsyncGpuReadback();
        _captureReadback = new AsyncGpuReadback();
        _diskCountLocation = GL.GetUniformLocation(_captureProgram, "uDiskCount");
        _neighborLocation = GL.GetUniformLocation(_captureProgram, "uMaxNeighbors");
        _deltaTimeLocation = GL.GetUniformLocation(_captureProgram, "uDeltaTime");

        var sessionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        _writer = new DatasetWriter(
            settings,
            sessionId,
            HashShader("Dataset/CaptureTraining.comp"),
            HashShader("Simulate.comp"));
        Console.WriteLine($"Coleta de dados: {_writer.FilePath}");
    }

    public bool IsEnabled => _enabled;
    public long SampleCount => _writer.SampleCount;
    public string FilePath => _writer.FilePath;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
    }

    public void Capture(
        int currentState,
        int outputState,
        int positionIn,
        int positionOut,
        int velocityIn,
        int velocityOut,
        int spinIn,
        int spinOut,
        int gridBuffer,
        int linksBuffer,
        int materialBuffer,
        float deltaTime)
    {
        if (!_enabled || ++_captureCounter < _settings.CaptureFrequency)
        {
            return;
        }
        if (_writer.SampleCount >= _settings.MaxSamples)
        {
            _enabled = false;
            Console.WriteLine($"Coleta encerrada: {_settings.MaxSamples} amostras registradas.");
            return;
        }
        _captureCounter = 0;
        _frameIndex++;

        ResetMetadata();
        GL.UseProgram(_captureProgram);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, positionIn);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, velocityIn);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, positionOut);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, velocityOut);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, gridBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5, linksBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7, materialBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 9, spinIn);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 10, spinOut);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 11, _captureBuffer);
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 12, _metadataBuffer);
        GL.Uniform1(_diskCountLocation, (uint)_settings.DiskCount);
        GL.Uniform1(_neighborLocation, (uint)_settings.MaxNeighbors);
        GL.Uniform1(_deltaTimeLocation, deltaTime);
        GL.DispatchCompute(
            (_settings.DiskCount + SimulationSettings.WorkGroupSize - 1)
            / SimulationSettings.WorkGroupSize,
            1,
            1);
        GL.MemoryBarrier(
            MemoryBarrierFlags.ShaderStorageBarrierBit
            | MemoryBarrierFlags.BufferUpdateBarrierBit);

        var captureBytes = _settings.DiskCount
            * DatasetSchema.FloatsPerSample
            * sizeof(float);
        GL.BindBuffer(BufferTarget.CopyReadBuffer, _metadataBuffer);
        GL.BindBuffer(BufferTarget.CopyWriteBuffer, _captureBuffer);
        GL.CopyBufferSubData(
            BufferTarget.CopyReadBuffer,
            BufferTarget.CopyWriteBuffer,
            0,
            captureBytes,
            16 * sizeof(uint));
        _captureReadback.Schedule(
            _captureBuffer,
            captureBytes + 16 * sizeof(uint),
            _frameIndex);
    }

    public void Poll()
    {
        if (_captureReadback.TryRead(out var combined, out var frameIndex))
        {
            var sampleFloatCount = _settings.DiskCount * DatasetSchema.FloatsPerSample;
            var samples = new float[sampleFloatCount];
            Array.Copy(combined, samples, sampleFloatCount);
            var metadata = new uint[16];
            for (var i = 0; i < metadata.Length; i++)
            {
                metadata[i] = BitConverter.SingleToUInt32Bits(combined[sampleFloatCount + i]);
            }
            var categories = new uint[6];
            var rampCount = metadata[2];
            var sphereCount = metadata[3];
            var multipleCount = metadata[4];
            for (var i = 0; i < categories.Length; i++)
            {
                categories[i] = i switch
                {
                    0 => (uint)Math.Max(0, _settings.DiskCount - (int)(rampCount + sphereCount - multipleCount)),
                    1 => rampCount,
                    2 => sphereCount,
                    3 => multipleCount,
                    4 => metadata[5],
                    _ => metadata[6],
                };
            }
            _writer.WriteBlock(samples, metadata[1], categories, frameIndex);
        }
    }

    private void ResetMetadata()
    {
        GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _metadataBuffer);
        uint[] zeros = new uint[16];
        GL.BufferSubData(
            BufferTarget.ShaderStorageBuffer,
            IntPtr.Zero,
            zeros.Length * sizeof(uint),
            zeros);
    }

    private static string HashShader(string fileName)
    {
        var source = ShaderSource.Load(fileName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        _captureReadback.Dispose();
        GL.DeleteBuffer(_captureBuffer);
        GL.DeleteBuffer(_metadataBuffer);
        GL.DeleteProgram(_captureProgram);
        _writer.Dispose();
    }
}
