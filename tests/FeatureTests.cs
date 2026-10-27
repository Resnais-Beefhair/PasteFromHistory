using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasteFromHistory.Core;
using PasteFromHistory.Services;
using PasteFromHistory.UI;

namespace PasteFromHistory.Tests;

internal static class FeatureTests
{
    internal static void Run(Action<bool, string> check, string output)
    {
        SettingsAndShortcuts(check, output);
        PlainTextAndExclusions(check);
        PinsAndRetention(check, output);
        StartupRetention(check, output);
    }

    private static void SettingsAndShortcuts(Action<bool, string> check, string output)
    {
        var path = Path.Combine(output, "settings-1.2-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\"MenuCount\":10,\"HistoryLimit\":100,\"HotkeyEnabled\":true}");
        var old = AppSettings.Load(path);
        check(old.HotkeyEnabled && old.Shortcut == HotkeyGesture.Default && old.RetentionDays == 0
            && old.ExcludedProcesses.Length == 0, "1.1 settings preserve the shortcut and do not enable retention or exclusions");
        var configured = old with { HotkeyModifiers = 3, HotkeyKey = 0x4B, RetentionDays = 14,
            ExcludedProcesses = [" KeePass.exe ", "keepass", "KeePassXC.EXE"] };
        configured.Save(path);
        var loaded = AppSettings.Load(path);
        check(loaded.Shortcut == new HotkeyGesture(3, 0x4B) && loaded.RetentionDays == 14
            && loaded.ExcludedProcesses.Length == 2, "Custom shortcut, retention, and normalized exclusions survive restart");
        check(loaded.Shortcut.ToString() == "Ctrl+Alt+K" && new HotkeyGesture(7, 0x86).ToString() == "Ctrl+Alt+Shift+F23",
            "Custom shortcut labels reflect modifiers and function keys");
        foreach (var gesture in new[] { new HotkeyGesture(0, 0x41), new HotkeyGesture(4, 0x41),
            new HotkeyGesture(8, 0x56), new HotkeyGesture(3, 0x7B), new HotkeyGesture(3, 0), new HotkeyGesture(0x4002, 0x56) })
            check(!gesture.IsValid, "Unsafe or unsupported shortcut rejected: " + gesture);
        var invalid = new AppSettings { HotkeyEnabled = true, HotkeyModifiers = 4, RetentionDays = 99999 };
        invalid.Normalize();
        check(!invalid.HotkeyEnabled && invalid.Shortcut == HotkeyGesture.Default && invalid.RetentionDays == 3650,
            "Invalid saved shortcut is disabled and retention is bounded");

        var registered = new Dictionary<int, HotkeyGesture>();
        var operations = new List<string>();
        var blocked = new HotkeyGesture(3, 0x42);
        using var hotkey = new GlobalHotkey(0, (id, gesture) =>
        {
            operations.Add("register");
            if (gesture == blocked) return false;
            registered.Add(id, gesture); return true;
        }, id => { operations.Add("unregister"); registered.Remove(id); });
        check(hotkey.Set(true, HotkeyGesture.Default), "Initial shortcut can be registered");
        check(!hotkey.Set(true, blocked) && hotkey.Current == HotkeyGesture.Default && registered.Count == 1,
            "A shortcut conflict preserves the previous working registration");
        operations.Clear();
        check(hotkey.Set(true, loaded.Shortcut) && operations.SequenceEqual(new[] { "register", "unregister" })
            && registered.Count == 1, "Shortcut replacement reserves the new combination before releasing the old one");
        operations.Clear();
        check(hotkey.Set(true, loaded.Shortcut) && operations.Count == 0, "Saving an unchanged shortcut does not re-register it");
        check(hotkey.Set(false, loaded.Shortcut) && registered.Count == 0 && hotkey.Current == null,
            "Disabling the shortcut releases its registration");
        File.Delete(path);
    }

    private static void PlainTextAndExclusions(Action<bool, string> check)
    {
        var clip = new ClipEntry { Text = "Plain ä € 🙂\r\nSecond line", Html = "<b>Formatted</b>", Rtf = "{\\rtf1\\b Formatted}" };
        var plain = ClipboardCodec.Encode(clip, plainText: true);
        check(plain.TryGetData<string>(DataFormats.UnicodeText, out var plainText) && plainText == clip.Text
            && !plain.GetDataPresent(DataFormats.Html, false) && !plain.GetDataPresent(DataFormats.Rtf, false),
            "Plain paste preserves exact Unicode text and strips HTML and RTF");
        var normal = ClipboardCodec.Encode(clip);
        check(normal.TryGetData<string>(DataFormats.Html, out var html) && html == clip.Html
            && normal.TryGetData<string>(DataFormats.Rtf, out var rtf) && rtf == clip.Rtf,
            "Plain paste leaves the original formatted clip available for later normal paste");
        check(ClipboardCodec.Read(plain, new()) == null, "Plain-text writes retain the internal no-recapture marker");
        foreach (var kind in new[] { ClipKind.Image, ClipKind.Files })
        {
            var rejected = false;
            try { ClipboardCodec.Encode(new ClipEntry { Kind = kind }, true); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Plain-text mode cannot reinterpret a " + kind + " clip");
        }

        check(CaptureExclusions.TryParse(" KeePass.exe\r\nkeepass\n \nKeePassXC.EXE; 1Password.exe", out var names, out _)
            && names.Length == 3, "Exclusions normalize names, suffixes, whitespace, and duplicates");
        check(CaptureExclusions.ShouldSkip(names, "KEEPASS", "notepad"), "An excluded clipboard owner is blocked even when another app is active");
        check(CaptureExclusions.ShouldSkip(names, "helper", "KeePassXC.exe"), "An excluded foreground app blocks helper-owned clipboard contents");
        check(!CaptureExclusions.ShouldSkip(names, "keepass-not-a-match", "notepad"), "Process exclusion uses exact names rather than prefixes");
        check(CaptureExclusions.ShouldSkip(names, null, "notepad") && CaptureExclusions.ShouldSkip(names, "notepad", null),
            "Unknown owner or foreground is skipped while exclusions are configured");
        check(!CaptureExclusions.ShouldSkip([], null, null), "An empty exclusion list preserves ownerless clipboard capture");
        foreach (var value in new[] { @"C:\Apps\secret.exe", "secret*", "secret/child", ".exe" })
            check(!CaptureExclusions.TryParse(value, out _, out _), "Invalid exclusion input rejected: " + value);
        check(!CaptureExclusions.TryNormalize(Enumerable.Repeat("app", 101), out _, out _), "Exclusion list has a bounded size");
        check(CaptureExclusions.ShouldSkip([null], "notepad", "notepad"), "Malformed saved exclusion rules block capture instead of disabling protection");
        var reads = 0;
        IDataObject Read() { reads++; var data = new DataObject(); data.SetText("Allowed synthetic clip"); return data; }
        var settings = new AppSettings { ExcludedProcesses = names };
        check(ClipboardCodec.ReadFromSource(Read, settings, new(1, "keepass", "keepass")) == null && reads == 0,
            "Excluded applications are rejected before their clipboard contents are requested");
        check(ClipboardCodec.ReadFromSource(Read, settings, new(2, null, "notepad")) == null && reads == 0,
            "Unknown sources are rejected before clipboard contents are requested");
        check(ClipboardCodec.ReadFromSource(Read, settings, new(3, "notepad", "notepad"))?.Text == "Allowed synthetic clip" && reads == 1,
            "Known allowed sources still capture normally");
    }

    private static void PinsAndRetention(Action<bool, string> check, string output)
    {
        var now = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        ClipEntry Clip(string text, double days = 0, bool pinned = false) => new()
            { Text = text, CapturedAt = now.AddDays(-days), Pinned = pinned };
        var history = new History();
        var pin = Clip("A saved snippet", 20);
        history.Add(pin, 10);
        check(history.SetPinned(pin.Id, true, 10) == null, "A clip can be pinned");
        for (var i = 0; i < 25; i++) history.Add(Clip("Recent " + i), 10, 7, now);
        check(history.Entries.Count == 11 && history.Entries.Single(e => e.Id == pin.Id).Pinned,
            "Pinned clips survive both age and recent-count eviction");
        history.Add(Clip(pin.Text), 10, 7, now);
        check(history.Entries[0].Id == pin.Id && history.Entries[0].Pinned && history.Entries[0].CapturedAt == now,
            "Recopying a pinned clip preserves its identity and pin while refreshing its timestamp");
        var storage = new HistoryStorage(Path.Combine(output, "pin-roundtrip-" + Guid.NewGuid().ToString("N") + ".dat"));
        storage.Schedule(history.Entries, true); storage.Flush();
        check(storage.Load().Single(e => e.Id == pin.Id).Pinned, "Pin state survives encrypted storage and restart");
        check(!JsonSerializer.Deserialize<ClipEntry>("{\"Text\":\"Legacy clip\"}")!.Pinned, "A history record without Pinned loads as unpinned");

        var many = new History();
        for (var i = 0; i < History.MaxPinnedEntries; i++) many.Add(Clip("Pin " + i, pinned: true), 100);
        var overflow = Clip("Pin overflow"); many.Add(overflow, 100);
        check(many.SetPinned(overflow.Id, true, 100) != null && !many.Entries.Single(e => e.Id == overflow.Id).Pinned,
            "The 51st pin is refused without deleting or altering its clip");
        var big = new History();
        for (var i = 0; i < 2; i++) big.Add(Clip(new string((char)('a' + i), 4 * 1024 * 1024), pinned: true), 100);
        var small = Clip("Would exceed 16 MiB"); big.Add(small, 100);
        check(big.SetPinned(small.Id, true, 100) != null && big.Entries.Count(e => e.Pinned) == 2,
            "Pin byte budget is enforced independently of the pin count");
        for (var i = 0; i < 6; i++) big.Add(Clip(new string((char)('c' + i), 4 * 1024 * 1024)), 100);
        check(big.Entries.Sum(e => e.Size) <= History.MaxTotalBytes && big.Entries.Count(e => e.Pinned) == 2,
            "Large pins and recent clips share the bounded 48 MiB total budget");

        var retention = new History();
        var old = Clip("Expired", 7);
        var fresh = Clip("Just inside deadline") with { CapturedAt = now.AddDays(-7).AddTicks(1) };
        var pinnedOld = Clip("Old but pinned", 100, true);
        var future = Clip("Future clock", -1);
        retention.Load([old, fresh, pinnedOld, future], 100);
        var notifications = 0; retention.Changed += () => notifications++;
        retention.Trim(100, 7, now);
        check(retention.Entries.Select(e => e.Id).ToHashSet().SetEquals(new[] { fresh.Id, pinnedOld.Id, future.Id }) && notifications == 1,
            "Expiration uses an exact age boundary, preserves pins and future timestamps, and notifies persistence");
        retention.Trim(100, 7, now);
        check(notifications == 1, "A retention sweep with no removals does not trigger redundant saves");
        check(retention.SetPinned(pinnedOld.Id, false, 100, 7, now) == null && retention.Entries.All(e => e.Id != pinnedOld.Id),
            "Unpinning restores the age policy immediately");
        var disabled = new History(); disabled.Load([old], 100, 0, now);
        check(disabled.Entries.Count == 1, "Zero retention days leaves old clips intact");
        disabled.Add(Clip(old.Text), 100, 7, now);
        check(disabled.Entries.Count == 1 && disabled.Entries[0].CapturedAt == now, "Recopying restarts an unpinned clip's retention age");
        history.Clear(); check(history.Entries.Count == 0, "Explicit clear removes pinned and unpinned clips");

        var invalidPath = Path.Combine(output, "over-budget-pins-" + Guid.NewGuid().ToString("N") + ".dat");
        var invalidPins = Enumerable.Range(0, 51).Select(i => Clip("Invalid pin " + i, pinned: true)).ToArray();
        var original = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(invalidPins), Encoding.UTF8.GetBytes("Leikehistoria.storage.v1"), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(invalidPath, original);
        var invalidStorage = new HistoryStorage(invalidPath); var rejected = false;
        try { invalidStorage.Load(); } catch (InvalidDataException) { rejected = true; }
        invalidStorage.Schedule([], true); invalidStorage.Flush();
        check(rejected && File.ReadAllBytes(invalidPath).SequenceEqual(original), "An invalid saved pin budget preserves the original database instead of dropping pins");
    }

    private static void StartupRetention(Action<bool, string> check, string output)
    {
        var directory = Path.Combine(output, "retention-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        new AppSettings { CompanionMenu = false, ExplorerMenu = false, HotkeyEnabled = false, RetentionDays = 1 }
            .Save(Path.Combine(directory, "settings.json"));
        var now = DateTimeOffset.UtcNow;
        var pin = new ClipEntry { Text = "Keep this pinned fixture", Pinned = true, CapturedAt = now.AddDays(-10) };
        var storage = new HistoryStorage(Path.Combine(directory, "history.dat"));
        storage.Schedule([pin, new ClipEntry { Text = "Expired before startup", CapturedAt = now.AddDays(-2) },
            new ClipEntry { Text = "Fresh on startup", CapturedAt = now }], true); storage.Flush();
        var previousContext = SynchronizationContext.Current;
        try
        {
            using var instance = new SingleInstance(directory);
            using var app = new TrayApplication(instance, new LaunchRequest("background", null, 0), directory, testMode: true);
            app.PruneHistory(now.AddDays(2)); // The same entry point as the periodic sweep.
            app.ExitThread();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        check(storage.Load().Select(e => e.Id).SequenceEqual(new[] { pin.Id }),
            "Startup and periodic expiration persist removals while keeping old pinned clips across exit");
    }

    internal static void RunUi(Action<bool, string> check, Action<Form, string> render)
    {
        var history = new History();
        var pinned = new ClipEntry { Text = "Frequently used reply", Pinned = true, CapturedAt = DateTimeOffset.Now.AddDays(-3) };
        var plainClip = new ClipEntry { Text = "A formatted sample", Rtf = "{\\rtf1 styled}" };
        history.Add(pinned, 100); history.Add(plainClip, 100);
        history.Add(new ClipEntry { Kind = ClipKind.Files, Paths = [@"C:\Examples\notes.txt"] }, 100);
        using (var form = new HistoryForm(history, new(), false))
        {
            render(form, "features-history.png");
            var list = Controls(form).OfType<ListBox>().Single();
            Button Button(string title) => Controls(form).OfType<Button>().Single(button => button.Text == title);
            check(((ClipEntry)list.Items[0]).Kind == ClipKind.Files, "Recent history stays chronological instead of promoting older favorites");
            list.SelectedItem = plainClip;
            (ClipEntry? Entry, bool Paste, bool Plain) chosen = default;
            form.Chosen += (entry, paste, plain) => chosen = (entry, paste, plain);
            Button("Paste as text").PerformClick();
            check(chosen.Entry?.Id == plainClip.Id && chosen.Paste && chosen.Plain, "Plain-text button requests plain paste of the selected clip");
            Button("Paste").PerformClick();
            check(chosen.Paste && !chosen.Plain, "Normal paste button keeps formatting");
            Button("Pin").PerformClick();
            check(history.Entries.Single(e => e.Id == plainClip.Id).Pinned && list.SelectedItem is ClipEntry { Pinned: true },
                "Pin button updates history and preserves selection");
            Button("Favorites").PerformClick();
            check(list.Items.Count == 2 && list.Items.Cast<ClipEntry>().All(e => e.Pinned), "Pinned filter shows only pinned clips");
            Button("Recent").PerformClick();
            list.SelectedItem = history.Entries.Single(e => e.Kind == ClipKind.Files);
            check(!Button("Paste as text").Enabled, "Plain-text action is disabled for file clips");
            form.Size = form.MinimumSize;
            render(form, "features-history-small.png");
            check(Controls(form).OfType<Button>().Where(button => button.Visible).All(button => button.Parent!.ClientRectangle.Contains(button.Bounds)),
                "History actions fit the minimum window size");
            check(list.ClientSize.Height >= list.ItemHeight * 3, "Minimum history window keeps at least three visible clip rows at the active DPI");
        }
        var original = new AppSettings(); AppSettings? accepted = null;
        using var settings = new SettingsForm(original, value => { accepted = value; return null; });
        render(settings, "settings.png");
        var tabs = Controls(settings).OfType<TabControl>().Single();
        tabs.SelectedIndex = 1; render(settings, "settings-access.png");
        var boxes = Controls(settings).OfType<CheckBox>().ToArray();
        boxes.Single(box => box.Text == "Enable global shortcut").Checked = true;
        boxes.Single(box => box.Text == "Alt").Checked = true;
        boxes.Single(box => box.Text == "Shift").Checked = false;
        var keys = Controls(settings).OfType<ComboBox>().Single();
        keys.SelectedItem = keys.Items.Cast<object>().Single(key => key.ToString() == "K");
        Controls(settings).OfType<NumericUpDown>().Single(number => number.AccessibleName == "Expire unpinned clips after (days)").Value = 14;
        tabs.SelectedIndex = 2;
        boxes.Single(box => box.Text == "Remember the source app for new clips").Checked = true;
        Controls(settings).OfType<TextBox>().Single(box => box.AccessibleName == "Excluded applications").Text = "KeePass.exe\r\nKeePassXC.exe";
        render(settings, "settings-privacy.png");
        tabs.SelectedIndex = 3; render(settings, "settings-about.png");
        check(tabs.SelectedTab!.Text == "About" && settings.Text.Contains("1.3.0")
            && Controls(settings).OfType<Button>().Any(button => button.Text == "Check for updates" && button.Enabled),
            "About shows the installed version and an explicit update check without starting a request");
        Controls(settings).OfType<Button>().Single(button => button.Text == "Save").PerformClick();
        check(accepted is { HotkeyEnabled: true, RetentionDays: 14 } && accepted.Shortcut == new HotkeyGesture(3, 0x4B)
            && accepted.ExcludedProcesses.Length == 2 && accepted.RememberSourceApp, "Settings UI saves history, access and opt-in source metadata together");
        check(!original.HotkeyEnabled && original.RetentionDays == 0 && original.ExcludedProcesses.Length == 0,
            "Editing settings does not mutate the currently active settings before save");
    }

    private static IEnumerable<Control> Controls(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Controls(child)) yield return descendant; }
    }
}
