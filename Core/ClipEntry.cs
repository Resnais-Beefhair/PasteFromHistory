using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PasteFromHistory.Core;

public enum ClipKind { Text, Image, Files }

public sealed record ClipEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ClipKind Kind { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string Text { get; init; } = "";
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public byte[]? Png { get; init; }
    public string[] Paths { get; init; } = [];
    public int Width { get; init; }
    public int Height { get; init; }

    [JsonIgnore] public string KindLabel => Kind switch
    {
        ClipKind.Image => "IMAGE", ClipKind.Files => "FILES", _ => "TEXT"
    };
    [JsonIgnore] public string Title => Kind switch
    {
        ClipKind.Image => $"Image · {Width:N0} × {Height:N0}",
        ClipKind.Files => Paths.Length == 1 ? Path.GetFileName(Paths[0].TrimEnd('\\')) : $"{Paths.Length} files or folders",
        _ => OneLine(Text)
    };
    [JsonIgnore] public string Detail => Kind switch
    {
        ClipKind.Image => $"PNG · {((Png?.Length ?? 0) / 1024d):N0} KiB",
        ClipKind.Files => string.Join(" · ", Paths.Take(3).Select(Path.GetFileName)),
        _ => $"{Text.Length:N0} characters" + (Rtf != null || Html != null ? " · formatted" : "")
    };
    [JsonIgnore] public long Size => 2L * (Text.Length + (Html?.Length ?? 0) + (Rtf?.Length ?? 0)
        + Paths.Sum(p => p.Length)) + (Png?.Length ?? 0);

    public string Fingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        Add(Kind.ToString());
        Add(Text); Add(Html ?? ""); Add(Rtf ?? "");
        foreach (var path in Paths) Add(path);
        if (Png != null) hash.AppendData(Png);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public bool Matches(string query) => string.IsNullOrWhiteSpace(query)
        || Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Paths.Any(p => p.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    public override string ToString() => Title;

    private static string OneLine(string value)
    {
        var preview = value[..Math.Min(value.Length, 240)];
        return string.Join(" ", preview.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
