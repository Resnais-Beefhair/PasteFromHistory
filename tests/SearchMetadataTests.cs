using System.Diagnostics;
using System.Text.Json;
using PasteFromHistory.Core;
using PasteFromHistory.Services;

namespace PasteFromHistory.Tests;

internal static class SearchMetadataTests
{
    internal static void Run(Action<bool, string> check)
    {
        Metadata(check);
        Search(check);
        SourceCapture(check);
        SearchBudget(check);
    }

    private static void Metadata(Action<bool, string> check)
    {
        var legacy = JsonSerializer.Deserialize<ClipEntry>("{\"Text\":\"Legacy content\"}")!;
        check(History.IsValid(legacy) && legacy.Name == "" && legacy.Collection == "" && legacy.SourceApp == "",
            "Legacy clips load with empty optional metadata");
        var history = new History();
        history.Add(legacy, 100);
        var notifications = 0;
        history.Changed += () => notifications++;
        var originalFingerprint = legacy.Fingerprint();
        check(history.SetMetadata(legacy.Id, "  Billing details  ", "  Work  ") == null && notifications == 1,
            "Naming a clip trims labels and notifies persistence");
        var renamed = history.Entries.Single();
        check(renamed.Title == "Billing details" && renamed.Collection == "Work" && renamed.Text == legacy.Text
            && renamed.CapturedAt == legacy.CapturedAt && renamed.Fingerprint() == originalFingerprint,
            "Names change display and search without changing content, copy date, or duplicate identity");
        check(renamed.Size == legacy.Size + 2L * (renamed.Name.Length + renamed.Collection.Length),
            "Metadata contributes to history byte limits");
        check(history.SetMetadata(legacy.Id, renamed.Name, renamed.Collection) == null && notifications == 1,
            "Saving unchanged labels does not cause redundant persistence");
        check(history.SetMetadata(legacy.Id, new string('n', 121), "") != null
            && history.SetMetadata(legacy.Id, "", new string('c', 61)) != null
            && history.SetMetadata(legacy.Id, "Two\nlines", "") != null && notifications == 1,
            "Overlong and multiline labels are rejected without modifying history");
        check(history.SetMetadata(Guid.NewGuid(), "Missing", "") != null,
            "Naming an expired or deleted clip fails visibly");
        history.SetPinned(legacy.Id, true, 100);
        history.Add(legacy with { Id = Guid.NewGuid(), CapturedAt = legacy.CapturedAt.AddMinutes(1), SourceApp = "notepad" }, 100);
        var recopied = history.Entries.Single();
        check(recopied.Id == legacy.Id && recopied.Pinned && recopied.Name == "Billing details"
            && recopied.Collection == "Work" && recopied.SourceApp == "notepad",
            "Recopy preserves labels and favorite identity while recording an opted-in source");
        history.Add(legacy with { Id = Guid.NewGuid() }, 100);
        check(history.Entries.Single().SourceApp == "notepad", "A recopy without source metadata does not invent a new source");
        var json = JsonSerializer.Serialize(history.Entries.Single());
        var reloaded = JsonSerializer.Deserialize<ClipEntry>(json)!;
        check(reloaded.Name == "Billing details" && reloaded.Collection == "Work" && reloaded.SourceApp == "notepad"
            && reloaded.Pinned && History.IsValid(reloaded), "Clip metadata round-trips through the storage serialization format");
        check(history.SetMetadata(legacy.Id, "", "") == null && history.Entries.Single().Title == legacy.Title,
            "Clearing a custom name restores the content preview title");
        foreach (var invalid in new[] { legacy with { Name = null! }, legacy with { Collection = null! },
            legacy with { SourceApp = null! }, legacy with { Name = new string('n', 121) },
            legacy with { Collection = "first\rsecond" }, legacy with { SourceApp = new string('s', 261) } })
            check(!History.IsValid(invalid), "Malformed saved metadata is rejected before size or display access");

        var bounded = new History();
        var maximum = new ClipEntry { Text = new string('x', (int)History.MaxEntryBytes / 2) };
        bounded.Add(maximum, 100);
        check(bounded.SetMetadata(maximum.Id, "Name", "") != null && bounded.Entries.Single().Name == "",
            "Adding a name cannot bypass the per-clip byte limit");
        var pinBudget = new History();
        pinBudget.Add(maximum with { Pinned = true }, 100);
        var second = maximum with { Id = Guid.NewGuid(), Text = new string('y', maximum.Text.Length - 100), Pinned = true };
        pinBudget.Add(second, 100);
        pinBudget.Add(new ClipEntry { Text = new string('p', 100), Pinned = true }, 100);
        check(pinBudget.SetMetadata(second.Id, "x", "") != null && pinBudget.Entries.Single(e => e.Id == second.Id).Name == "",
            "Editing favorite metadata cannot exceed the favorite byte budget");
        var totalBudget = new History();
        // One name still fits in the entry but would exceed the aggregate 48 MiB cap.
        var fullEntry = maximum with { Id = Guid.NewGuid(), Text = maximum.Text[..^100] };
        totalBudget.Add(fullEntry, 100);
        for (var i = 0; i < 5; i++) totalBudget.Add(maximum with { Id = Guid.NewGuid(), Text = new string((char)('k' + i), maximum.Text.Length) }, 100);
        totalBudget.Add(new ClipEntry { Text = new string('t', 100) }, 100);
        check(totalBudget.SetMetadata(fullEntry.Id, "n", "") != null
            && totalBudget.Entries.Count == 7 && totalBudget.Entries.Single(e => e.Id == fullEntry.Id).Name == "",
            "Metadata admission never silently evicts another clip when the aggregate budget is full");
    }

    private static void Search(Action<bool, string> check)
    {
        var clip = new ClipEntry { Text = "The annual invoice contains delivery details.", Name = "Billing address",
            Collection = "Clients", SourceApp = "EXCEL" };
        check(clip.Matches("details annual") && clip.Matches("ANNUAL address clients excel"),
            "Search terms match in any order across content, name, collection, and source");
        check(!clip.Matches("annual absentword"), "Every search term must match");
        check(new ClipEntry { Text = "Ångström Ömega" }.Matches("öMEGA ångström"), "Multi-term search preserves Unicode case handling");
        foreach (var typo in new[] { "invocie", "invoce", "invoicce", "invoixe" })
            check(clip.Matches(typo), "Typo search accepts one edit or transposition: " + typo);
        check(!clip.Matches("ivoxce") && !new ClipEntry { Text = "cat" }.Matches("bat")
            && !new ClipEntry { Text = "12345678" }.Matches("12345679"),
            "Multiple typos, short words, and near-looking numbers do not match approximately");
        var fuzzy = new ClipEntry { Text = "Invoice details", Pinned = true };
        var exact = new ClipEntry { Text = "The invocie field is intentional" };
        var results = HistorySearch.Find(new[] { fuzzy, exact }, "invocie").ToArray();
        check(results.Select(e => e.Id).SequenceEqual(new[] { exact.Id, fuzzy.Id }), "Exact results rank ahead of typo matches regardless of pin state");
        check(HistorySearch.Find(new[] { clip, exact, fuzzy }, "   ").Select(e => e.Id).SequenceEqual(new[] { clip.Id, exact.Id, fuzzy.Id }),
            "An empty search preserves recent order without moving favorites first");
        check(new ClipEntry { Kind = ClipKind.Files, Paths = [@"C:\Examples\annual-report.txt"] }.Matches("report annual"),
            "File paths remain searchable across multiple terms");
        var longQuery = new string('q', HistorySearch.MaxQueryLength + 1);
        check(new ClipEntry { Text = longQuery }.Matches(longQuery) && !new ClipEntry { Text = longQuery[..^1] }.Matches(longQuery),
            "Oversized queries use full exact text without silently dropping suffixes");
        var manyTerms = string.Join(' ', Enumerable.Range(0, HistorySearch.MaxTerms + 1).Select(i => "term" + i));
        check(new ClipEntry { Text = manyTerms }.Matches(manyTerms)
            && !new ClipEntry { Text = manyTerms.Replace("term16", "missing", StringComparison.Ordinal) }.Matches(manyTerms),
            "Queries exceeding the term budget remain exact rather than ignoring later terms");
        var large = new ClipEntry { Text = new string('z', HistorySearch.MaxFuzzyCharacters + 10) + " invoice", Name = "Budget record" };
        check(large.Matches("invoice") && large.Matches("budegt") && !large.Matches("invocie"),
            "Exact search covers long content while bounded typo search includes labels and skips the distant tail");
        check(!new ClipEntry { Text = "invoiceX123" }.Matches("invocie"), "Typo search compares complete words rather than arbitrary prefixes");
        check(!new ClipEntry { Text = "build1" }.Matches("built"), "Typo search does not substitute digits in identifiers");
    }

    private static void SourceCapture(Action<bool, string> check)
    {
        IDataObject Data() { var value = new DataObject(); value.SetText("Synthetic source example"); return value; }
        var source = new ClipboardSource(1, "notepad", "unrelated");
        check(!new AppSettings().RememberSourceApp && ClipboardCodec.ReadFromSource(Data, new(), source)?.SourceApp == "",
            "Source-app recording is opt-in for both new and legacy settings");
        var enabled = new AppSettings { RememberSourceApp = true };
        check(ClipboardCodec.ReadFromSource(Data, enabled, source)?.SourceApp == "notepad",
            "Source capture stores the clipboard owner process name rather than the foreground app");
        check(ClipboardCodec.ReadFromSource(Data, enabled, new(1, null, "foreground"))?.SourceApp == "",
            "Unknown clipboard owners do not receive a guessed source app");
        var reads = 0;
        IDataObject CountRead() { reads++; return Data(); }
        check(ClipboardCodec.ReadFromSource(CountRead, enabled with { ExcludedProcesses = ["notepad"] }, source) == null && reads == 0,
            "Opt-in source labels do not bypass exclusions or read blocked contents");
        var privateData = (DataObject)Data(); privateData.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream([1]));
        check(ClipboardCodec.ReadFromSource(() => privateData, enabled, source) == null,
            "Source-attributed capture still honors clipboard privacy markers");
        check(ClipboardCodec.ReadFromSource(Data, enabled, source with { Owner = new string('s', 261) }) is { SourceApp: "", Text: "Synthetic source example" },
            "Invalid optional source metadata is dropped without losing valid clipboard contents");
        var legacySettings = JsonSerializer.Deserialize<AppSettings>("{\"HistoryLimit\":100}")!;
        check(!legacySettings.RememberSourceApp, "Updating legacy settings never silently enables source capture");
    }

    private static void SearchBudget(Action<bool, string> check)
    {
        const int count = 1000;
        const int characters = (int)(History.MaxTotalBytes / 2 / count);
        var template = string.Concat(Enumerable.Repeat("invoice delivery annual address payment reference ", 600));
        var entries = Enumerable.Range(0, count).Select(index => new ClipEntry
            { Text = template[..(characters - 4)] + index.ToString("D4") }).ToArray();
        const string query = "invxxxx delxxxx annxxx addxxxx payxxxx refxxxx zbxxxxx zcxxxxx zdxxxxx zexxxxx zfxxxxx zgxxxxx zhxxxxx zixxxxx zjxxxxx zkxxxxx";
        _ = HistorySearch.Find(entries.Take(2), query).Count();
        var timer = Stopwatch.StartNew();
        var results = HistorySearch.Find(entries, query).ToArray();
        timer.Stop();
        Console.WriteLine($"Search budget: {count} clips / {entries.Sum(entry => entry.Size):N0} bytes / {timer.Elapsed.TotalMilliseconds:N0} ms");
        check(results.Length == 0 && timer.Elapsed < TimeSpan.FromSeconds(2),
            "A maximum-size history handles a sixteen-term miss query within the bounded search budget");
    }
}
