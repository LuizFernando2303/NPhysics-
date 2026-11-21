using System.Text;

namespace NPhysics;

internal sealed record DatasetMetadata(
    ushort SchemaVersion,
    int DiskCount,
    int Seed,
    int CaptureFrequency,
    int MaxSamples,
    int MaxNeighbors,
    float FixedStep,
    float Gravity,
    float InclinationDegrees,
    string SessionId,
    string ShaderHash,
    string SolverHash);

internal sealed class DatasetReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;

    public DatasetReader(string filePath)
    {
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: false);
    }

    public DatasetMetadata ReadMetadata()
    {
        _stream.Position = 0;
        if (_reader.ReadUInt32() != DatasetSchema.Magic)
        {
            throw new InvalidDataException("Arquivo .nphysdata invalido.");
        }

        var schemaVersion = _reader.ReadUInt16();
        if (schemaVersion != DatasetSchema.SchemaVersion
            || _reader.ReadUInt16() != sizeof(float)
            || _reader.ReadUInt16() != DatasetSchema.FloatsPerSample)
        {
            throw new InvalidDataException("Formato do cabecalho do dataset nao suportado.");
        }

        var diskCount = _reader.ReadInt32();
        var seed = _reader.ReadInt32();
        var captureFrequency = _reader.ReadInt32();
        var maxSamples = _reader.ReadInt32();
        var maxNeighbors = _reader.ReadInt32();
        var fixedStep = _reader.ReadSingle();
        var gravity = _reader.ReadSingle();
        var inclinationDegrees = _reader.ReadSingle();
        var sessionId = ReadFixedString(64);
        var shaderHash = ReadFixedString(64);
        var solverHash = ReadFixedString(64);
        if (diskCount <= 0
            || captureFrequency <= 0
            || maxSamples <= 0
            || maxNeighbors <= 0
            || !float.IsFinite(fixedStep)
            || fixedStep <= 0f
            || !float.IsFinite(gravity)
            || !float.IsFinite(inclinationDegrees))
        {
            throw new InvalidDataException("Metadados invalidos no cabecalho do dataset.");
        }

        return new DatasetMetadata(
            schemaVersion,
            diskCount,
            seed,
            captureFrequency,
            maxSamples,
            maxNeighbors,
            fixedStep,
            gravity,
            inclinationDegrees,
            sessionId,
            shaderHash,
            solverHash);
    }

    public IEnumerable<(uint FrameIndex, float[] Samples, uint OverflowCount, uint[] Categories)> ReadBlocks()
    {
        _stream.Position = 0;
        var magic = _reader.ReadUInt32();
        if (magic != DatasetSchema.Magic)
        {
            throw new InvalidDataException("Arquivo .nphysdata invalido.");
        }
        _stream.Position = DatasetSchema.HeaderSize;

        while (_stream.Position < _stream.Length)
        {
            var blockHeaderSize = _reader.ReadInt32();
            if (blockHeaderSize != DatasetSchema.BlockHeaderSize)
            {
                throw new InvalidDataException("Bloco de dataset com tamanho invalido.");
            }
            var frameIndex = _reader.ReadUInt32();
            var sampleCount = _reader.ReadUInt32();
            var floatCount = _reader.ReadUInt32();
            var overflowCount = _reader.ReadUInt32();
            var categories = new uint[6];
            for (var i = 0; i < categories.Length; i++)
            {
                categories[i] = _reader.ReadUInt32();
            }
            _reader.ReadUInt32();
            _reader.ReadUInt32();
            _reader.ReadUInt32();
            _reader.ReadUInt32();
            _reader.ReadUInt32();
            var expectedFloatCount = checked(
                (long)sampleCount * DatasetSchema.FloatsPerSample);
            if (expectedFloatCount > int.MaxValue
                || floatCount != (uint)expectedFloatCount)
            {
                throw new InvalidDataException("Quantidade de floats inconsistente no bloco.");
            }
            var bytesRemaining = _stream.Length - _stream.Position;
            var payloadBytes = checked((long)floatCount * sizeof(float));
            if (payloadBytes < 0 || payloadBytes > bytesRemaining)
            {
                throw new EndOfStreamException("Bloco de dataset truncado.");
            }
            var samples = new float[floatCount];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = _reader.ReadSingle();
                if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i]))
                {
                    throw new InvalidDataException("Amostra invalida encontrada no dataset.");
                }
            }
            yield return (frameIndex, samples, overflowCount, categories);
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
    }

    private string ReadFixedString(int byteCount)
    {
        var bytes = _reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
        {
            throw new EndOfStreamException("Cabecalho de dataset truncado.");
        }
        var length = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }
}
