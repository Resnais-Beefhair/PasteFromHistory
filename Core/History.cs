namespace PasteFromHistory.Core;

public sealed class History
{
    public const long MaxEntryBytes = 8L * 1024 * 1024;
    public const long MaxTotalBytes = 48L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxPinnedEntries = 50;
    public const long MaxPinnedBytes = 16L * 1024 * 1024;
    private readonly List<ClipEntry> entries = [];
    private readonly Dictionary<Guid, string> fingerprints = [];
    public IReadOnlyList<ClipEntry> Entries => entries;
    public event Action? Changed;

    public bool Add(ClipEntry entry, int limit, int retentionDays = 0, DateTimeOffset? now = null)
    {
        if (!IsValid(entry)) return false;
        var fingerprint = entry.Fingerprint();
        var duplicate = entries.FindIndex(e => fingerprints[e.Id] == fingerprint);
        if (duplicate >= 0) entry = entry with { Id = entries[duplicate].Id, Pinned = entries[duplicate].Pinned };
        if (entry.Pinned && PinLimitError(entry, duplicate >= 0 ? entries[duplicate].Id : null) != null) return false;
        if (!entry.Pinned && IsExpired(entry, retentionDays, now ?? DateTimeOffset.UtcNow)) return false;
        if (entries.Any(e => e.Id == entry.Id) && duplicate < 0) return false;
        if (duplicate >= 0)
        {
            fingerprints.Remove(entries[duplicate].Id);
            entries.RemoveAt(duplicate);
        }
        entries.Insert(0, entry);
        fingerprints[entry.Id] = fingerprint;
        TrimCore(limit, retentionDays, now ?? DateTimeOffset.UtcNow);
        Changed?.Invoke();
        return true;
    }

    public void Load(IEnumerable<ClipEntry> saved, int limit, int retentionDays = 0, DateTimeOffset? now = null)
    {
        entries.Clear(); fingerprints.Clear();
        var timestamp = now ?? DateTimeOffset.UtcNow;
        foreach (var entry in saved.Reverse()) Add(entry, limit, retentionDays, timestamp);
    }

    public void Trim(int limit, int retentionDays = 0, DateTimeOffset? now = null)
    {
        if (TrimCore(limit, retentionDays, now ?? DateTimeOffset.UtcNow)) Changed?.Invoke();
    }

    private bool TrimCore(int limit, int retentionDays, DateTimeOffset now)
    {
        limit = Math.Clamp(limit, 10, 1000);
        var changed = false;
        foreach (var expired in entries.Where(entry => !entry.Pinned && IsExpired(entry, retentionDays, now)).ToArray())
        {
            entries.Remove(expired); fingerprints.Remove(expired.Id); changed = true;
        }
        var bytes = entries.Sum(e => e.Size);
        var unpinnedCount = entries.Count(entry => !entry.Pinned);
        while (unpinnedCount > limit || bytes > MaxTotalBytes)
        {
            var index = entries.FindLastIndex(entry => !entry.Pinned);
            if (index < 0) break; // Pin admission always bounds the pinned set to 16 MiB.
            var last = entries[index];
            bytes -= last.Size;
            entries.RemoveAt(index);
            fingerprints.Remove(last.Id);
            unpinnedCount--; changed = true;
        }
        return changed;
    }

    public string? SetPinned(Guid id, bool pinned, int limit, int retentionDays = 0, DateTimeOffset? now = null)
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return "The clip is no longer in history.";
        var entry = entries[index];
        if (entry.Pinned == pinned) return null;
        if (pinned && PinLimitError(entry, entry.Id) is { } error) return error;
        entries[index] = entry with { Pinned = pinned };
        TrimCore(limit, retentionDays, now ?? DateTimeOffset.UtcNow);
        Changed?.Invoke();
        return null;
    }

    private string? PinLimitError(ClipEntry entry, Guid? except)
    {
        var pinned = entries.Where(e => e.Pinned && e.Id != except).ToArray();
        if (pinned.Length >= MaxPinnedEntries) return $"You can pin up to {MaxPinnedEntries} clips. Unpin a clip first.";
        if (pinned.Sum(e => e.Size) + entry.Size > MaxPinnedBytes) return "Pinned clips can use up to 16 MiB. Unpin a clip first.";
        return null;
    }

    private static bool IsExpired(ClipEntry entry, int days, DateTimeOffset now) => days > 0
        && now - entry.CapturedAt >= TimeSpan.FromDays(Math.Clamp(days, 1, 3650));

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
