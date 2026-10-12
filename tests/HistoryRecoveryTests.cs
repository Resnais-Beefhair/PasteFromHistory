using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasteFromHistory.Core;
using PasteFromHistory.Services;

namespace PasteFromHistory.Tests;

internal static class HistoryRecoveryTests
{
    public static void Run(Action<bool, string> check, string output)
    {
        var directory = Path.Combine(output, "recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.dat");
        byte[] original = [1, 2, 3, 4];
        var clip = new ClipEntry { Text = "New history after recovery" };
        var storage = new HistoryStorage(path);
        File.WriteAllBytes(path, original);
        check(LoadFails(storage), "Unreadable history reports a load failure");
        storage.Schedule([clip], true); storage.Flush();
        check(File.ReadAllBytes(path).SequenceEqual(original), "Failed load blocks overwriting the original");
        storage.Schedule([], false); storage.Flush();
        check(File.ReadAllBytes(path).SequenceEqual(original), "Failed load also blocks deleting the original");
        var backup = storage.Quarantine();
        check(!File.Exists(path) && File.ReadAllBytes(backup).SequenceEqual(original), "Quarantine preserves the original bytes beside history.dat");
        storage.Schedule([clip], true); storage.Flush();
        check(storage.Load().Single().Text == clip.Text && File.ReadAllBytes(backup).SequenceEqual(original), "New history saves after successful quarantine");
        storage.Schedule([], false); storage.Flush();
        check(!File.Exists(path) && File.Exists(backup), "Disabling persistence preserves the recovery copy");

        File.WriteAllBytes(path, original);
        check(LoadFails(storage), "A later unreadable history is detected");
        var secondBackup = storage.Quarantine();
        check(backup != secondBackup && File.ReadAllBytes(backup).SequenceEqual(original)
            && File.ReadAllBytes(secondBackup).SequenceEqual(original), "Repeated recovery never replaces an earlier backup");

        File.WriteAllBytes(path, original);
        check(LoadFails(storage), "Locked recovery fixture is unreadable");
        var moveFailed = false;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { storage.Quarantine(); } catch (IOException) { moveFailed = true; }
        }
        storage.Schedule([clip], true); storage.Schedule([], false); storage.Flush();
        check(moveFailed && File.ReadAllBytes(path).SequenceEqual(original), "Failed quarantine keeps saves and deletes blocked after the lock is released");
        storage.Quarantine();
        storage.Schedule([clip], true); storage.Flush();
        check(storage.Load().Single().Text == clip.Text, "A successful recovery retry allows saving again");

        var deniedStorage = new HistoryStorage(path);
        var originalCiphertext = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            check(LoadFails(deniedStorage), "An inaccessible history is not mistaken for a missing file");
        deniedStorage.Schedule([], true); deniedStorage.Flush();
        check(File.ReadAllBytes(path).SequenceEqual(originalCiphertext), "An inaccessible history stays protected after access returns");

        foreach (var json in new[] { "null", "[null]", "{\"Entries\":[]}", "not json",
            JsonSerializer.Serialize(new[] { clip, clip with { Text = "Duplicate ID" } }) })
        {
            var invalidStorage = new HistoryStorage(path);
            var invalid = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Encoding.UTF8.GetBytes("Leikehistoria.storage.v1"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, invalid);
            var failed = LoadFails(invalidStorage);
            invalidStorage.Schedule([], true); invalidStorage.Flush();
            check(failed && File.ReadAllBytes(path).SequenceEqual(invalid), "Invalid encrypted history cannot be silently replaced: " + json[..Math.Min(json.Length, 24)]);
        }

        CheckStartup(check, directory, original, locked: false);
        CheckStartup(check, directory, original, locked: true);
    }

    private static bool LoadFails(HistoryStorage storage)
    {
        try { storage.Load(); return false; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or JsonException) { return true; }
    }

    private static void CheckStartup(Action<bool, string> check, string root, byte[] original, bool locked)
    {
        var directory = Path.Combine(root, locked ? "locked-startup" : "startup");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.dat");
        File.WriteAllBytes(path, original);
        new AppSettings { CompanionMenu = false, ExplorerMenu = false, HotkeyEnabled = false }
            .Save(Path.Combine(directory, "settings.json"));
        var previousContext = SynchronizationContext.Current;
        try
        {
            using var fileLock = locked ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
            using var instance = new SingleInstance(directory);
            using var app = new TrayApplication(instance, new LaunchRequest("background", null, 0), directory, testMode: true);
            app.ExitThread();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        var backups = Directory.GetFiles(directory, "history.dat.unreadable-*");
        if (locked)
            check(backups.Length == 0 && File.ReadAllBytes(path).SequenceEqual(original), "Startup and immediate exit preserve a file that cannot be quarantined");
        else
            check(backups.Length == 1 && File.ReadAllBytes(backups[0]).SequenceEqual(original)
                && new HistoryStorage(path).Load().Length == 0, "Startup and immediate exit quarantine unreadable history before any save");
    }
}
