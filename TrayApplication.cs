using PasteFromHistory.Core;
using PasteFromHistory.Services;
using PasteFromHistory.UI;

namespace PasteFromHistory;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly string settingsPath;
    private readonly bool testMode;
    private AppSettings settings;
    private readonly History history = new();
    private readonly HistoryStorage storage;
    private readonly MessageWindow messages;
    private readonly MouseObserver mouse;
    private readonly MenuCompanion companion = new();
    private readonly EditableTargetProbe editableTarget;
    private readonly object traceGate = new();
    private readonly System.Windows.Forms.Timer rightClickDelay = new() { Interval = 220 };
    private readonly System.Windows.Forms.Timer foregroundTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer retentionTimer = new() { Interval = 60_000 };
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly ToolStripMenuItem pauseItem;
    private HistoryForm? fullHistory;
    private HistoryForm? popup;
    private SettingsForm? settingsWindow;
    private nint lastTarget;
    private sealed record EditorClick(Point Point, PasteTarget Target, Task<bool> Editable);
    private EditorClick? rightDownClick, pendingClick;
    private long clickGeneration;
    private bool exiting;
    private bool pasting;
    private string? startupNotice;

    public TrayApplication(SingleInstance instance, LaunchRequest request, string dataDirectory, bool testMode = false)
    {
        this.testMode = testMode;
        // Expose transient test windows to desktop accessibility tooling. Production
        // popups remain absent from the taskbar and do not activate on display.
        if (testMode) companion.ShowInTaskbar = true;
        settingsPath = Path.Combine(dataDirectory, "settings.json");
        editableTarget = new(trace: testMode ? Trace : null);
        try { settings = AppSettings.Load(settingsPath); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { settings = new(); startupNotice = "Settings could not be read. The app is using its defaults."; }
        storage = new HistoryStorage(Path.Combine(dataDirectory, "history.dat"));
        if (settings.SaveHistory)
        {
            try { history.Load(storage.Load(), settings.HistoryLimit, settings.RetentionDays); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                try
                {
                    var backup = storage.Quarantine();
                    startupNotice = "Saved history could not be opened. The original was preserved at:\n" + backup + "\nNew clips will start a new history.";
                }
                catch (Exception preserveError) when (preserveError is IOException or UnauthorizedAccessException)
                {
                    startupNotice = "Saved history could not be opened or moved. Saving is disabled for this session to protect the original:\n"
                        + Path.Combine(dataDirectory, "history.dat");
                }
            }
        }
        messages = new MessageWindow { Settings = () => settings };
        messages.Captured += entry => history.Add(entry, settings.HistoryLimit, settings.RetentionDays);
        messages.Hotkey += () =>
        {
            if (settingsWindow is { IsDisposed: false, Visible: true }) return;
            Trace("hotkey received"); ShowPopup(PasteTarget.Capture(Native.Root(Native.GetForegroundWindow())), Cursor.Position);
        };
        history.Changed += () => { storage.Schedule(history.Entries, settings.SaveHistory); UpdateTrayText(); };
        storage.Failed += message => Dispatch(() => Notify(message));

        icon = Theme.CreateIcon();
        var menu = new ContextMenuStrip { Font = Theme.Body };
        menu.Items.Add("Open history", null, (_, _) => ShowHistory(new PasteTarget(lastTarget)));
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        pauseItem = new ToolStripMenuItem("Pause capture", null, (_, _) => TogglePause());
        menu.Items.Add(pauseItem);
        menu.Items.Add("Clear history…", null, (_, _) => ClearHistory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = icon, Text = "PasteFromHistory", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowHistory(new PasteTarget(lastTarget)); };

        mouse = new MouseObserver(messages);
        mouse.RightButtonDown += (point, target) =>
        {
            if (CanOfferCompanion(target))
                rightDownClick = new(point, target, editableTarget.CheckAsync(point, target.Window));
        };
        mouse.RightClick += OnRightClick;
        mouse.ButtonDown += point =>
        {
            Trace("mouse down");
            if (!companion.Visible || !companion.Bounds.Contains(point)) companion.Hide();
            CancelPendingClick();
        };
        rightClickDelay.Tick += async (_, _) =>
        {
            rightClickDelay.Stop();
            var pending = pendingClick;
            pendingClick = null;
            if (pending == null) { Trace("right-click timer reject=no-pending-click"); return; }
            var generation = clickGeneration;
            var menu = Native.FindMenu(pending.Target.Window, pending.Point, testMode ? Trace : null);
            var editable = await pending.Editable;
            if (testMode)
            {
                var foreground = Native.GetForegroundWindow();
                Trace($"right-click timer editable={editable} exiting={exiting} generation-match={generation == clickGeneration}"
                    + $" can-offer={CanOfferCompanion(pending.Target)} foreground-class={Native.ClassName(foreground)}"
                    + $" target-class={Native.ClassName(pending.Target.Window)} same-root={Native.Root(foreground) == Native.Root(pending.Target.Window)}"
                    + $" foreground-own={Native.IsOwnWindow(foreground)} target-own={Native.IsOwnWindow(pending.Target.Window)}"
                    + $" menu-found={menu != null} menu-visible={menu != null && Native.IsWindowVisible(menu.Value.Handle)}"
                    + $" escape-down={(Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0}");
            }
            if (editable && !exiting && generation == clickGeneration && CanOfferCompanion(pending.Target)
                && Native.IsTargetForeground(pending.Target.Window)
                && (menu == null || Native.IsWindowVisible(menu.Value.Handle))
                && (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) == 0)
            {
                companion.ShowBeside(pending.Point, pending.Target);
                Trace("companion shown: visible=" + companion.Visible + ", native=" + Native.IsWindowVisible(companion.Handle) + ", bounds=" + companion.Bounds);
            }
        };
        companion.OpenHistory += async (target, point) =>
        {
            var window = target.Window;
            var foreground = Native.GetForegroundWindow();
            if (!Native.IsTargetForeground(window) && !Native.IsOwnWindow(foreground)) return;
            Native.SetForegroundWindow(window);
            Native.SendEscape();
            await Task.Delay(90);
            if (!exiting) ShowPopup(target, point);
        };
        foregroundTimer.Tick += (_, _) => RememberForeground();
        foregroundTimer.Start();
        retentionTimer.Tick += (_, _) => PruneHistory();
        retentionTimer.Start();
        messages.StartListening();
        if (ApplySettings(settings, initial: true) is { } failure) startupNotice = (startupNotice == null ? "" : startupNotice + "\n") + failure;
        if (!CaptureExclusions.TryNormalize(settings.ExcludedProcesses, out _, out _))
            startupNotice = (startupNotice == null ? "" : startupNotice + "\n") + "Capture is blocked because the excluded application list is invalid. Correct it in Settings > Privacy.";
        UpdateTrayText();
        instance.Listen(incoming => Dispatch(() => Handle(incoming)));
        Dispatch(() =>
        {
            if (startupNotice != null) Notify(startupNotice);
            Handle(request);
        });
    }

    private void Dispatch(Action action)
    {
        if (exiting || messages.IsDisposed) return;
        try { messages.BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    private void RememberForeground()
    {
        var window = Native.Root(Native.GetForegroundWindow());
        var className = Native.ClassName(window);
        if (window != 0 && !Native.IsOwnWindow(window)
            && className is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "#32768"))
            lastTarget = window;
    }

    private void OnRightClick(Point point, PasteTarget target)
    {
        var down = rightDownClick;
        rightDownClick = null;
        if (testMode) Trace($"right-click up down-found={down != null} target-match={down?.Target.Window == target.Window}"
            + $" can-offer={CanOfferCompanion(target)} target-class={Native.ClassName(target.Window)} target-own={Native.IsOwnWindow(target.Window)}");
        if (down == null || down.Target.Window != target.Window || !CanOfferCompanion(target)) return;
        // A right-button drag is not a request for the editor's context menu.
        var drag = SystemInformation.DragSize;
        if (testMode) Trace($"right-click movement dx={Math.Abs(point.X - down.Point.X)} dy={Math.Abs(point.Y - down.Point.Y)} limit={drag.Width},{drag.Height}");
        if (Math.Abs(point.X - down.Point.X) > drag.Width || Math.Abs(point.Y - down.Point.Y) > drag.Height) return;
        pendingClick = down with { Point = point };
        rightClickDelay.Stop(); rightClickDelay.Start();
    }

    private void CancelPendingClick()
    {
        clickGeneration++;
        rightClickDelay.Stop(); pendingClick = null; rightDownClick = null;
    }

    private bool CanOfferCompanion(PasteTarget target)
    {
        var window = target.Window;
        if (exiting || !settings.CompanionMenu || window == 0 || Native.IsOwnWindow(window)
            || popup is { IsDisposed: false, Visible: true }) return false;
        var className = Native.ClassName(window);
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "#32768") return false;
        // The desktop already has a real shell verb; normal applications get a companion.
        return !(settings.ExplorerMenu && className is "Progman" or "WorkerW");
    }

    private void Trace(string message)
    {
        if (!testMode) return;
        try
        {
            lock (traceGate)
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(settingsPath)!, "ui-diagnostics.txt"), DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { /* Diagnostic output is best effort and never interrupts input. */ }
    }

    public void Handle(LaunchRequest request)
    {
        var target = new PasteTarget((nint)request.Target);
        switch (request.Action)
        {
            case "quit": ExitThread(); break;
            case "background": break;
            case "settings": ShowSettings(); break;
            case "folder":
                if (request.Folder == null || !Directory.Exists(request.Folder)) { Notify("The destination folder could not be found."); return; }
                ShowPopup(target with { Folder = Path.GetFullPath(request.Folder) }, Cursor.Position); break;
            case "popup": ShowPopup(target, Cursor.Position); break;
            default: ShowHistory(target.Window == 0 ? target with { Window = lastTarget } : target); break;
        }
    }

    private void ShowPopup(PasteTarget target, Point location)
    {
        Trace("show popup requested");
        Trace("target focus before popup: " + Native.ClassName(Native.FocusedWindow(target.Window)));
        if (pasting) return;
        PruneHistory();
        CancelPendingClick(); companion.Hide(); popup?.Close();
        var form = new HistoryForm(history, settings, true, target.Folder);
        if (testMode)
        {
            form.Deactivate += (_, _) => Trace("popup deactivated foreground=" + Native.ClassName(Native.GetForegroundWindow()));
            form.FormClosed += (_, e) => Trace("popup closed reason=" + e.CloseReason + ", foreground=" + Native.ClassName(Native.GetForegroundWindow()));
        }
        if (testMode) form.ShowInTaskbar = true;
        popup = form;
        form.Chosen += (entry, paste, plainText) => Choose(form, entry, target, paste, plainText);
        form.FullHistoryRequested += () => { form.Close(); ShowHistory(target); };
        form.ClearRequested += ClearHistory;
        var work = Screen.FromPoint(location).WorkingArea;
        form.Height = Math.Min(form.Height, work.Height - 20);
        form.Location = new Point(Math.Clamp(location.X, work.Left, Math.Max(work.Left, work.Right - form.Width)),
            Math.Clamp(location.Y, work.Top, Math.Max(work.Top, work.Bottom - form.Height)));
        form.Show(); form.Activate();
        Trace("popup shown: visible=" + form.Visible + ", disposed=" + form.IsDisposed + ", foreground=" + Native.ClassName(Native.GetForegroundWindow()));
    }

    private void ShowHistory(PasteTarget target)
    {
        CancelPendingClick();
        PruneHistory();
        companion.Hide(); popup?.Close();
        if (fullHistory is { IsDisposed: false }) { fullHistory.Close(); }
        var form = new HistoryForm(history, settings, false, target.Folder);
        fullHistory = form;
        form.Chosen += (entry, paste, plainText) => Choose(form, entry, target, paste, plainText);
        form.SettingsRequested += ShowSettings;
        form.ClearRequested += ClearHistory;
        form.Show(); form.Activate();
    }

    private async void Choose(HistoryForm form, ClipEntry entry, PasteTarget target, bool shouldPaste, bool plainText)
    {
        Trace("entry chosen: paste=" + shouldPaste + ", target-own=" + Native.IsOwnWindow(target.Window));
        if (pasting) return;
        PruneHistory();
        if (!history.Entries.Any(current => current.Id == entry.Id)) { Notify("The clip has expired or was removed from history."); return; }
        pasting = true;
        try
        {
            if (!shouldPaste) { ClipboardCodec.Write(entry); Notify("Copied to the clipboard."); return; }
            form.Close();
            var result = await PasteService.Paste(entry, target, plainText);
            Trace("paste completed: success=" + (result == null) + ", foreground=" + Native.ClassName(Native.GetForegroundWindow())
                + ", target focus=" + Native.ClassName(Native.FocusedWindow(target.Window)));
            if (result != null) Notify(result);
        }
        catch (OperationCanceledException) { Notify("Copy cancelled. Files already copied remain in the destination."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or ArgumentException)
        { Notify("Paste failed. " + ex.Message); }
        finally { pasting = false; }
    }

    private void ShowSettings()
    {
        CancelPendingClick();
        companion.Hide(); popup?.Close();
        if (settingsWindow is { IsDisposed: false }) { settingsWindow.Activate(); return; }
        using var form = new SettingsForm(settings, value => ApplySettings(value, initial: false));
        settingsWindow = form;
        if (form.ShowDialog(fullHistory is { IsDisposed: false } ? fullHistory : null) == DialogResult.OK)
        {
            if (fullHistory is { IsDisposed: false }) ShowHistory(new PasteTarget(lastTarget));
        }
        settingsWindow = null;
    }

    private string? ApplySettings(AppSettings value, bool initial)
    {
        CancelPendingClick(); companion.Hide();
        value.Normalize();
        if (!messages.SetHotkey(value.HotkeyEnabled, value.Shortcut))
        {
            var failure = $"{value.Shortcut} could not be registered. Choose a different shortcut in Settings > Access.";
            if (!initial) return failure + " Your settings were not changed.";
            startupNotice = (startupNotice == null ? "" : startupNotice + "\n") + failure;
            value = value with { HotkeyEnabled = false };
        }
        try
        {
            if (!testMode)
            {
                SystemIntegration.SetExplorerMenu(value.ExplorerMenu);
                SystemIntegration.SetStartup(value.StartWithWindows);
            }
            mouse.SetEnabled(value.CompanionMenu);
            value.SetupComplete = true;
            value.Save(settingsPath);
            settings = value;
            PruneHistory();
            // Also persist startup expiration so an old saved clip cannot reappear.
            storage.Schedule(history.Entries, settings.SaveHistory);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            messages.SetHotkey(settings.HotkeyEnabled, settings.Shortcut);
            return "Some settings could not be applied. " + ex.Message;
        }
    }

    internal void PruneHistory(DateTimeOffset? now = null) => history.Trim(settings.HistoryLimit, settings.RetentionDays, now);

    private void TogglePause()
    {
        messages.Paused = !messages.Paused;
        pauseItem.Text = messages.Paused ? "Resume capture" : "Pause capture";
        pauseItem.Checked = messages.Paused;
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        if (tray != null) tray.Text = messages.Paused ? "PasteFromHistory · capture paused" : $"PasteFromHistory · {history.Entries.Count} clips";
    }

    private void ClearHistory()
    {
        if (MessageBox.Show("Clear all clipboard history, including pinned clips? The current Windows clipboard will stay unchanged.",
            "PasteFromHistory", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) history.Clear();
    }

    private void Notify(string message)
    {
        if (exiting) return;
        tray.ShowBalloonTip(4000, "PasteFromHistory", message, ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        if (pasting) { Notify("Wait for the file copy to finish, or cancel the copy first."); return; }
        exiting = true;
        PruneHistory();
        mouse.Dispose(); rightClickDelay.Dispose(); foregroundTimer.Dispose(); retentionTimer.Dispose();
        companion.Dispose(); popup?.Dispose(); fullHistory?.Dispose(); settingsWindow?.Dispose();
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon.Dispose();
        storage.Schedule(history.Entries, settings.SaveHistory); storage.Flush();
        messages.Dispose();
        base.ExitThreadCore();
    }
}
