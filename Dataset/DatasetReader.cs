using System.Text;

namespace NPhysics;

internal sealed class DatasetReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;

    public DatasetReader(string filePath)
    {
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: false);
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
            if (floatCount != sampleCount * DatasetSchema.FloatsPerSample)
            {
                throw new InvalidDataException("Quantidade de floats inconsistente no bloco.");
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
}
