using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PasteFromHistory.Core;
using PasteFromHistory.Services;

namespace PasteFromHistory.Tests;

internal static class FeatureInteractionTests
{
    internal static void Run(Action<bool, string> check, string output)
    {
        using var foreground = new Form { Text = "PasteFromHistory – shortcut regression", Size = new Size(380, 160) };
        foreground.Show(); foreground.Activate(); Application.DoEvents();
        check(Native.GetForegroundWindow() == foreground.Handle, "Shortcut test owns the foreground window");
        using var listener = new MessageWindow();
        using var blocker = new Form();
        var first = new HotkeyGesture(7, 0x86); // Ctrl+Alt+Shift+F23, no common app-local action.
        var second = new HotkeyGesture(7, 0x87);
        var count = 0;
        listener.Hotkey += () => count++;
        check(listener.SetHotkey(true, first), "Custom shortcut registers with Windows");
        try
        {
            check(Native.RegisterHotKey(blocker.Handle, 41, second.Modifiers, second.Key), "Conflict fixture reserves a second shortcut");
            check(!listener.SetHotkey(true, second), "Windows rejects an occupied custom shortcut");
            Press(first);
            check(WaitUntil(() => count == 1), "Previous shortcut still delivers WM_HOTKEY after a failed replacement");
            Native.UnregisterHotKey(blocker.Handle, 41);
            check(listener.SetHotkey(true, second), "Shortcut can be replaced after the conflict is released");
            Press(second);
            check(WaitUntil(() => count == 2), "Replacement shortcut delivers WM_HOTKEY");
            check(Native.RegisterHotKey(blocker.Handle, 42, first.Modifiers, first.Key), "Replacing the shortcut releases its previous keys");
            check(listener.SetHotkey(false, second)
                && Native.RegisterHotKey(blocker.Handle, 43, second.Modifiers, second.Key), "Disabling the shortcut releases its new keys");
        }
        finally
        {
            foreach (var id in new[] { 41, 42, 43 }) Native.UnregisterHotKey(blocker.Handle, id);
        }
        foreground.Close();
        PasteIntoAnotherProcess(check, output);
    }

    private static void PasteIntoAnotherProcess(Action<bool, string> check, string output)
    {
        // A separate process exercises the production focus/SendInput path, which
        // deliberately refuses to inject paste into one of the app's own windows.
        var result = Path.Combine(output, "paste-target-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--paste-target"); start.ArgumentList.Add(result);
        using var child = Process.Start(start)!;
        try
        {
            check(WaitUntil(() => { child.Refresh(); return child.MainWindowHandle != 0 && File.Exists(result); }), "Separate rich-text paste target starts");
            var window = child.MainWindowHandle;
            Native.SetForegroundWindow(window);
            check(WaitUntil(() => Native.GetForegroundWindow() == window), "Separate paste target receives foreground focus");
            var target = PasteTarget.Capture(window);
            check(target.Focus != 0 && Native.Root(target.Focus) == window, "Paste target captures the original rich-text control");
            var entry = new ClipEntry { Text = "A bold sample", Rtf = "{\\rtf1\\ansi\\b A bold sample}" };
            var plain = PasteService.Paste(entry, target, plainText: true);
            check(WaitUntil(() => plain.IsCompleted) && plain.GetAwaiter().GetResult() == null
                && WaitUntil(() => ReadResult(result) is { Text: "A bold sample", Bold: false }), "Plain-text paste reaches the other process without bold formatting");
            SendMessage(target.Focus, 0x000C, 0, ""); // WM_SETTEXT must be sent for a control in another process.
            check(WaitUntil(() => ReadResult(result)?.Text == ""), "Rich-text fixture resets between paste modes");
            var normal = PasteService.Paste(entry, target);
            check(WaitUntil(() => normal.IsCompleted) && normal.GetAwaiter().GetResult() == null
                && WaitUntil(() => ReadResult(result) is { Text: "A bold sample", Bold: true }), "Normal paste still restores RTF formatting in the other process");

            SendMessage(target.Focus, 0x000C, 0, "");
            check(WaitUntil(() => ReadResult(result)?.Text == ""), "Rich-text fixture resets before transformed paste");
            var transformed = entry with { Text = TextTransforms.Apply(entry.Text, entry.Html, TextTransformKind.Uppercase), Html = null, Rtf = null };
            var transformedPaste = PasteService.Paste(transformed, target, plainText: true);
            check(WaitUntil(() => transformedPaste.IsCompleted) && transformedPaste.GetAwaiter().GetResult() == null
                && WaitUntil(() => ReadResult(result) is { Text: "A BOLD SAMPLE", Bold: false })
                && entry.Text == "A bold sample" && entry.Rtf != null,
                "Transformed text reaches another process while the stored original keeps its contents and formatting");

            using var capture = new MessageWindow();
            var processName = Process.GetCurrentProcess().ProcessName;
            capture.Settings = () => new AppSettings { ExcludedProcesses = [processName] };
            var captured = 0; capture.Captured += _ => captured++; capture.StartListening();
            Clipboard.SetText("Excluded synthetic capture fixture");
            var source = ClipboardSource.Capture();
            check(source.Owner == processName && source.Foreground == processName, "Clipboard owner and foreground resolve to actual executable names");
            PumpFor(350);
            check(captured == 0, "Configured process exclusion blocks a real clipboard update");
            capture.Settings = () => new AppSettings { ExcludedProcesses = ["nonexistent-excluded-fixture"] };
            Clipboard.SetText("Allowed synthetic capture fixture");
            check(WaitUntil(() => captured == 1), "Known allowed processes capture through the real clipboard listener");
        }
        finally
        {
            // Only synthetic contents are written; no user's clipboard content is
            // read, logged, or retained by this fixture. Clear our last test clip.
            ClipboardCodec.Write(new ClipEntry { Text = "PasteFromHistory test completed" });
            if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(3000)) child.Kill(); }
        }
    }

    internal static void RunTarget(string result)
    {
        using var form = new Form { Text = "PasteFromHistory – rich-text paste target", Size = new Size(420, 180) };
        using var field = new RichTextBox { Dock = DockStyle.Fill };
        form.Controls.Add(field);
        void Save()
        {
            var selection = field.SelectionStart;
            field.SelectAll();
            var snapshot = new Snapshot(field.Text, field.SelectionFont?.Bold == true);
            field.Select(selection, 0);
            File.WriteAllText(result, JsonSerializer.Serialize(snapshot));
        }
        field.TextChanged += (_, _) => Save();
        form.Shown += (_, _) => { field.Focus(); Save(); };
        Application.Run(form);
    }

    private sealed record Snapshot(string Text, bool Bold);
    private static Snapshot? ReadResult(string path)
    {
        try { return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    private static void Press(HotkeyGesture gesture)
    {
        var keys = new[] { Native.Key(0x11), Native.Key(0x12), Native.Key(0x10), Native.Key((ushort)gesture.Key),
            Native.Key((ushort)gesture.Key, true), Native.Key(0x10, true), Native.Key(0x12, true), Native.Key(0x11, true) };
        if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf<Native.INPUT>()) != keys.Length)
            throw new InvalidOperationException("Windows rejected the synthetic shortcut input.");
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents(); if (condition()) return true; Thread.Sleep(15);
        }
        return false;
    }
    private static void PumpFor(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(15); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint wParam, string text);
}
