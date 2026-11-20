namespace NPhysics;

internal static class DatasetSchema
{
    public const uint Magic = 0x31544850u;
    public const ushort SchemaVersion = 1;
    public const ushort FloatsPerSample = 384;
    public const int HeaderSize = 512;
    public const int BlockHeaderSize = 64;

    public static readonly uint[] CategoryCounts = new uint[6];
}
