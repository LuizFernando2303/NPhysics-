using System.Text;

namespace NPhysics.Tests;

public sealed class DatasetValidationTests
{
    [Fact]
    public void ReaderRejectsTruncatedBlockPayload()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(
                File.Create(path),
                Encoding.UTF8,
                leaveOpen: false))
            {
                writer.Write(new byte[DatasetSchema.HeaderSize]);
                writer.Write(DatasetSchema.BlockHeaderSize);
                writer.Write((uint)1);
                writer.Write((uint)1);
                writer.Write((uint)DatasetSchema.FloatsPerSample);
            }

            using var reader = new DatasetReader(path);
            Assert.ThrowsAny<Exception>(() => reader.ReadBlocks().ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReaderRejectsNaNSample()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(
                File.Create(path),
                Encoding.UTF8,
                leaveOpen: false))
            {
                writer.Write(new byte[DatasetSchema.HeaderSize]);
                writer.Write(DatasetSchema.BlockHeaderSize);
                writer.Write((uint)1);
                writer.Write((uint)1);
                writer.Write((uint)DatasetSchema.FloatsPerSample);
                writer.Write((uint)0);
                for (var i = 0; i < 6; i++) writer.Write((uint)0);
                for (var i = 0; i < 5; i++) writer.Write((uint)0);
                for (var i = 0; i < DatasetSchema.FloatsPerSample; i++)
                {
                    writer.Write(i == 0 ? float.NaN : 0f);
                }
            }

            using var reader = new DatasetReader(path);
            Assert.Throws<InvalidDataException>(() => reader.ReadBlocks().ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
