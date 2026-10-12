namespace PasteFromHistory.Core;

public sealed class History
{
    public const long MaxEntryBytes = 8L * 1024 * 1024;
    public const long MaxTotalBytes = 48L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    private readonly List<ClipEntry> entries = [];
    private readonly Dictionary<Guid, string> fingerprints = [];
    public IReadOnlyList<ClipEntry> Entries => entries;
    public event Action? Changed;

    public bool Add(ClipEntry entry, int limit)
    {
        if (!IsValid(entry)) return false;
        var fingerprint = entry.Fingerprint();
        var duplicate = entries.FindIndex(e => fingerprints[e.Id] == fingerprint);
        if (duplicate >= 0)
        {
            fingerprints.Remove(entries[duplicate].Id);
            entries.RemoveAt(duplicate);
        }
        entries.Insert(0, entry);
        fingerprints[entry.Id] = fingerprint;
        Trim(limit);
        Changed?.Invoke();
        return true;
    }

    public void Load(IEnumerable<ClipEntry> saved, int limit)
    {
        entries.Clear(); fingerprints.Clear();
        foreach (var entry in saved.Reverse()) Add(entry, limit);
    }

    public void Trim(int limit)
    {
        limit = Math.Clamp(limit, 10, 1000);
        var bytes = entries.Sum(e => e.Size);
        while (entries.Count > limit || bytes > MaxTotalBytes)
        {
            var last = entries[^1];
            bytes -= last.Size;
            entries.RemoveAt(entries.Count - 1);
            fingerprints.Remove(last.Id);
        }
    }

    public void Remove(Guid id)
    {
        entries.RemoveAll(e => e.Id == id); fingerprints.Remove(id); Changed?.Invoke();
    }

    public void Clear() { entries.Clear(); fingerprints.Clear(); Changed?.Invoke(); }

    public static bool IsValid(ClipEntry? entry)
    {
        if (entry == null || entry.Text == null || entry.Paths == null || entry.Paths.Any(p => p == null)) return false;
        if (entry.Size > MaxEntryBytes || entry.Id == Guid.Empty) return false;
        return entry.Kind switch
        {
            ClipKind.Text => !string.IsNullOrEmpty(entry.Text),
            ClipKind.Image => entry.Png != null && PngHeader.TryReadSize(entry.Png, out var width, out var height)
                && width == entry.Width && height == entry.Height && (long)width * height <= MaxPixels,
            ClipKind.Files => entry.Paths.Length is > 0 and <= 1024 && entry.Paths.All(Path.IsPathFullyQualified),
            _ => false
        };
    }
}
