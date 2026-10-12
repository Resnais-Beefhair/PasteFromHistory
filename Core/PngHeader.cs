using System.Buffers.Binary;

namespace PasteFromHistory.Core;

internal static class PngHeader
{
    // Read only the signature and mandatory first IHDR chunk. The image decoder
    // still validates the image; this check bounds dimensions before decoding.
    public static bool TryReadSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = height = 0;
        if (png.Length < 33 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || BinaryPrimitives.ReadUInt32BigEndian(png[8..12]) != 13 || !png[12..16].SequenceEqual("IHDR"u8))
            return false;
        var w = BinaryPrimitives.ReadUInt32BigEndian(png[16..20]);
        var h = BinaryPrimitives.ReadUInt32BigEndian(png[20..24]);
        if (w is 0 or > int.MaxValue || h is 0 or > int.MaxValue) return false;
        width = (int)w;
        height = (int)h;
        return true;
    }
}
