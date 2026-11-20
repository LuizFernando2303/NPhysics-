using System.Text;

namespace NPhysics;

internal sealed class DatasetWriter : IDisposable
{
    private readonly string _filePath;
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly object _sync = new();
    private long _sampleCount;
    private bool _disposed;

    public DatasetWriter(
        DatasetCaptureSettings settings,
        string sessionId,
        string shaderHash,
        string solverHash)
    {
        Directory.CreateDirectory(settings.OutputDirectory);
        _filePath = Path.Combine(settings.OutputDirectory, $"{sessionId}.nphysdata");
        _stream = new FileStream(_filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: false);
        WriteHeader(settings, sessionId, shaderHash, solverHash);
    }

    public string FilePath => _filePath;
    public long SampleCount => _sampleCount;

    public void WriteBlock(
        float[] samples,
        int sampleFloatCount,
        uint overflowCount,
        uint[] categories,
        uint frameIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleFloatCount);
        if (sampleFloatCount > samples.Length
            || sampleFloatCount % DatasetSchema.FloatsPerSample != 0)
        {
            throw new ArgumentException("O bloco possui um tamanho de amostra invalido.");
        }
        ArgumentNullException.ThrowIfNull(categories);
        if (categories.Length != 6)
        {
            throw new ArgumentException("O bloco precisa conter seis contadores de categoria.");
        }

        lock (_sync)
        {
            _writer.Write(DatasetSchema.BlockHeaderSize);
            _writer.Write((uint)frameIndex);
            _writer.Write((uint)(sampleFloatCount / DatasetSchema.FloatsPerSample));
            _writer.Write((uint)sampleFloatCount);
            _writer.Write(overflowCount);
            for (var i = 0; i < 6; i++)
            {
                _writer.Write(categories[i]);
            }
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            _writer.Write((uint)0);
            for (var i = 0; i < sampleFloatCount; i++)
            {
                var value = samples[i];
                _writer.Write(value);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new InvalidDataException("A captura encontrou NaN ou Infinity.");
                }
            }
            _writer.Flush();
            _sampleCount += sampleFloatCount / DatasetSchema.FloatsPerSample;
        }
    }

    private void WriteHeader(
        DatasetCaptureSettings settings,
        string sessionId,
        string shaderHash,
        string solverHash)
    {
        _writer.Write(DatasetSchema.Magic);
        _writer.Write(DatasetSchema.SchemaVersion);
        _writer.Write((ushort)sizeof(float));
        _writer.Write(DatasetSchema.FloatsPerSample);
        _writer.Write(settings.DiskCount);
        _writer.Write(settings.Seed);
        _writer.Write(settings.CaptureFrequency);
        _writer.Write(settings.MaxSamples);
        _writer.Write(settings.MaxNeighbors);
        _writer.Write(SimulationSettings.FixedStep);
        _writer.Write(SimulationSettings.Gravity);
        _writer.Write(SimulationSettings.InclinationDegrees);
        WriteFixedString(sessionId, 64);
        WriteFixedString(shaderHash, 64);
        WriteFixedString(solverHash, 64);

        var written = (int)_stream.Position;
        if (written > DatasetSchema.HeaderSize)
        {
            throw new InvalidOperationException("Cabecalho do dataset excede o tamanho reservado.");
        }
        _writer.Write(new byte[DatasetSchema.HeaderSize - written]);
    }

    private void WriteFixedString(string value, int byteCount)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        _writer.Write(bytes, 0, Math.Min(bytes.Length, byteCount));
        if (bytes.Length < byteCount)
        {
            _writer.Write(new byte[byteCount - bytes.Length]);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer.Dispose();
            _disposed = true;
        }
    }
}
