using PasteFromHistory.Core;

namespace PasteFromHistory.Tests;

internal static class ManualValidation
{
    internal static void Prepare(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Use a new empty directory for synthetic manual validation.");
        Directory.CreateDirectory(directory);
        new AppSettings { SetupComplete = true, CompanionMenu = true, ExplorerMenu = false, HotkeyEnabled = false,
            RememberSourceApp = true }.Save(Path.Combine(directory, "settings.json"));
        var entries = new[]
        {
            new ClipEntry { Text = "PasteFromHistory 1.3 validation — ä € 🙂", Name = "Validation text", SourceApp = "notepad", CapturedAt = DateTimeOffset.Now },
            new ClipEntry { Text = "Blue calendar meeting", Name = "Team appointment", Collection = "Work", SourceApp = "excel", CapturedAt = DateTimeOffset.Now.AddMinutes(-2) },
            new ClipEntry { Text = "First wrapped line\r\ncontinues here.\r\n\r\nSecond paragraph.", Name = "PDF paragraph", Collection = "Work", CapturedAt = DateTimeOffset.Now.AddMinutes(-3) },
            new ClipEntry { Text = "A bold word and a link", Html = "<p>A <strong>bold</strong> word and <a href=\"https://example.org/guide\">a link</a></p>", CapturedAt = DateTimeOffset.Now.AddMinutes(-4) },
            new ClipEntry { Text = "https://example.org/article?id=42&utm_source=newsletter#details", Name = "Article link", CapturedAt = DateTimeOffset.Now.AddMinutes(-5) },
            new ClipEntry { Text = "Hello! Here are the details we discussed.", Name = "Support reply", Collection = "Favorites", Pinned = true, CapturedAt = DateTimeOffset.Now.AddDays(-20) }
        };
        var storage = new HistoryStorage(Path.Combine(directory, "history.dat"));
        storage.Schedule(entries, true); storage.Flush();
        Console.WriteLine("Prepared synthetic manual validation profile.");
    }
}
