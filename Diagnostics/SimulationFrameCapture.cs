using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using OpenTK.Graphics.OpenGL4;

namespace NPhysics;

internal sealed class SimulationFrameCapture
{
    private const int MaximumCapturesPerSession = 20;
    private readonly string _outputDirectory;
    private int _captureCount;

    public SimulationFrameCapture(string tracePath)
    {
        var traceName = Path.GetFileNameWithoutExtension(tracePath);
        _outputDirectory = Path.Combine(Path.GetDirectoryName(tracePath)!, $"{traceName}-frames");
        Directory.CreateDirectory(_outputDirectory);
    }

    public int CaptureCount => _captureCount;
    public bool CanCapture => _captureCount < MaximumCapturesPerSession;

    public string Capture(int width, int height)
    {
        if (!CanCapture)
        {
            throw new InvalidOperationException(
                $"Limite de {MaximumCapturesPerSession} capturas por sessao atingido.");
        }

        var pixels = new byte[width * height * 4];
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        var fileName = $"frame-{++_captureCount:D2}.png";
        var outputPath = Path.Combine(_outputDirectory, fileName);
        WritePng(outputPath, pixels, width, height);
        return Path.Combine(Path.GetFileName(_outputDirectory), fileName);
    }

    private static void WritePng(string path, byte[] pixels, int width, int height)
    {
        var bytesPerRow = width * 4;
        var scanlines = new byte[height * (bytesPerRow + 1)];
        for (var y = 0; y < height; y++)
        {
            var sourceOffset = (height - 1 - y) * bytesPerRow;
            var destinationOffset = y * (bytesPerRow + 1);
            Array.Copy(pixels, sourceOffset, scanlines, destinationOffset + 1, bytesPerRow);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = UpdateCrc(0xffffffff, typeBytes);
        crc = UpdateCrc(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ~crc);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
            }
        }

        return crc;
    }
}
