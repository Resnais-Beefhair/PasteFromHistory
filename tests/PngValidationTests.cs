using System.Buffers.Binary;
using PasteFromHistory.Core;
using PasteFromHistory.Services;

namespace PasteFromHistory.Tests;

internal static class PngValidationTests
{
    public static void Run(Action<bool, string> check, byte[] validPng)
    {
        check(PngHeader.TryReadSize(validPng, out var width, out var height) && width == 72 && height == 48,
            "PNG dimensions are read in network byte order");
        byte[] Header(uint w, uint h)
        {
            var bytes = validPng[..33];
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), w);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), h);
            return bytes;
        }
        ClipEntry? Capture(byte[] bytes)
        {
            var data = new DataObject(); data.SetData("PNG", false, new MemoryStream(bytes));
            return ClipboardCodec.Read(data, new());
        }
        var badSignature = Header(72, 48); badSignature[0] = 0;
        var badChunk = Header(72, 48); badChunk[12] = (byte)'X';
        var badLength = Header(72, 48); badLength[11] = 12;
        foreach (var (name, bytes) in new[] {
            ("truncated header", validPng[..23]), ("bad signature", badSignature),
            ("missing first IHDR", badChunk), ("incorrect IHDR length", badLength),
            ("zero width", Header(0, 48)), ("zero height", Header(72, 0)),
            ("out-of-range dimension", Header(uint.MaxValue, 1)) })
            check(!PngHeader.TryReadSize(bytes, out _, out _) && Capture(bytes) == null, "PNG rejected before decoding: " + name);

        // Header-only fixtures would fail inside the image decoder. Rejection must
        // happen before that call and must not allocate a bitmap of these sizes.
        check(Capture(Header(10_000, 10_000)) == null, "100-megapixel PNG rejected without invoking the decoder");
        check(Capture(Header(int.MaxValue, int.MaxValue)) == null, "PNG pixel count cannot overflow into an accepted size");
        check(History.IsValid(new ClipEntry { Kind = ClipKind.Image, Png = Header(8_000, 5_000), Width = 8_000, Height = 5_000 }),
            "Image dimension guard includes the 40-million-pixel boundary");
        var forged = new ClipEntry { Kind = ClipKind.Image, Png = Header(10_000, 10_000), Width = 1, Height = 1 };
        check(!History.IsValid(forged), "Saved image metadata cannot hide oversized PNG dimensions");
        check(!History.IsValid(new ClipEntry { Kind = ClipKind.Image, Png = validPng, Width = 73, Height = 48 }),
            "Saved image dimensions must match its PNG header");
        var rejected = false;
        try { ClipboardCodec.Encode(forged); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Paste validates PNG dimensions before decoding");
        check(!History.IsValid(null), "Null history records are rejected safely");
    }
}
