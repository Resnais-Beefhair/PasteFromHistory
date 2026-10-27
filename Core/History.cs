namespace PasteFromHistory.Core;

public sealed class History
{
    public const long MaxEntryBytes = 8L * 1024 * 1024;
    public const long MaxTotalBytes = 48L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxPinnedEntries = 50;
    public const long MaxPinnedBytes = 16L * 1024 * 1024;
    public const int MaxNameLength = 120;
    public const int MaxCollectionLength = 60;
    public const int MaxSourceAppLength = 260;
    private readonly List<ClipEntry> entries = [];
    private readonly Dictionary<Guid, string> fingerprints = [];
    public IReadOnlyList<ClipEntry> Entries => entries;
    public event Action? Changed;

    public bool Add(ClipEntry entry, int limit, int retentionDays = 0, DateTimeOffset? now = null)
    {
        if (!IsValid(entry)) return false;
        var fingerprint = entry.Fingerprint();
        var duplicate = entries.FindIndex(e => fingerprints[e.Id] == fingerprint);
        if (duplicate >= 0)
        {
            var original = entries[duplicate];
            entry = entry with { Id = original.Id, Pinned = original.Pinned, Name = original.Name,
                Collection = original.Collection, SourceApp = entry.SourceApp.Length == 0 ? original.SourceApp : entry.SourceApp };
            // Metadata retained from an earlier copy still counts toward admission.
            if (!IsValid(entry)) return false;
        }
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

    public string? SetMetadata(Guid id, string name, string collection)
    {
        var index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return "The clip is no longer in history.";
        if (name == null || collection == null) return "Enter a name and collection, or leave them empty.";
        name = name.Trim(); collection = collection.Trim();
        if (name.Length > MaxNameLength) return $"Names can contain up to {MaxNameLength} characters.";
        if (collection.Length > MaxCollectionLength) return $"Collections can contain up to {MaxCollectionLength} characters.";
        if (name.Any(char.IsControl) || collection.Any(char.IsControl)) return "Names and collections must be a single line.";
        var original = entries[index];
        if (original.Name == name && original.Collection == collection) return null;
        var updated = original with { Name = name, Collection = collection };
        if (!IsValid(updated)) return "The clip and its name exceed the 8 MiB limit. Use a shorter name.";
        if (updated.Pinned && PinLimitError(updated, id) is { } pinError) return pinError;
        if (entries.Sum(entry => entry.Size) - original.Size + updated.Size > MaxTotalBytes)
            return "History has reached its 48 MiB limit. Delete an unneeded clip first.";
        // Renaming must not promote a clip, change its copy date, or evict another clip.
        entries[index] = updated;
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
        if (entry == null || entry.Text == null || entry.Paths == null || entry.Paths.Any(p => p == null)
            || entry.Name == null || entry.Collection == null || entry.SourceApp == null) return false;
        if (entry.Name.Length > MaxNameLength || entry.Collection.Length > MaxCollectionLength
            || entry.SourceApp.Length > MaxSourceAppLength || entry.Name.Any(char.IsControl)
            || entry.Collection.Any(char.IsControl) || entry.SourceApp.Any(char.IsControl)) return false;
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
