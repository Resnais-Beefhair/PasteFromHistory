using PasteFromHistory.Core;
using PasteFromHistory.Services;
using System.Diagnostics;
using System.Net.Http;

namespace PasteFromHistory.UI;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown menuCount, historyLimit, retentionDays;
    private readonly CheckBox companion, explorer, hotkey, startup, save, images, files, rememberSource;
    private readonly CheckBox controlKey, altKey, shiftKey;
    private readonly ComboBox shortcutKey;
    private readonly TextBox excluded;
    private readonly AppSettings original;
    private readonly Func<AppSettings, string?>? apply;
    public AppSettings Result { get; private set; }

    private sealed record KeyChoice(uint Code)
    {
        public override string ToString() => HotkeyGesture.KeyName(Code);
    }

    public SettingsForm(AppSettings settings, Func<AppSettings, string?>? apply = null)
    {
        original = settings; Result = settings with { }; this.apply = apply;
        Theme.Style(this);
        Text = $"PasteFromHistory {Application.ProductVersion.Split('+')[0]} settings"; ClientSize = new Size(700, 680);
        MinimumSize = new Size(620, 560); StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; Padding = new Padding(24);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        Controls.Add(root);
        var header = Theme.Label("Make it yours", Theme.Title); header.Dock = DockStyle.Fill;
        root.Controls.Add(header, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Body, AccessibleName = "Settings sections",
            DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(140, 34), SizeMode = TabSizeMode.Fixed };
        tabs.DrawItem += (_, e) =>
        {
            using var background = new SolidBrush(e.Index == tabs.SelectedIndex ? Theme.Hover : Theme.Surface);
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Theme.Body, e.Bounds, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        root.Controls.Add(tabs, 0, 1);
        TableLayoutPanel Page(string title)
        {
            var page = new TabPage(title) { BackColor = Theme.Back, Padding = new Padding(14) };
            tabs.TabPages.Add(page);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            page.Controls.Add(scroll);
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 16, 12) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(content);
            return content;
        }
        void Add(TableLayoutPanel page, Control control) { control.Margin = new Padding(0, 6, 0, 6); page.Controls.Add(control); }
        void Help(TableLayoutPanel page, string text)
        {
            var label = Theme.Label(text, Theme.Small, Theme.Muted);
            label.MaximumSize = new Size(530, 0); Add(page, label);
        }
        void Section(TableLayoutPanel page, string title)
        {
            var label = Theme.Label(title, Theme.Small, Theme.Accent); label.Margin = new Padding(0, 13, 0, 6); page.Controls.Add(label);
        }
        CheckBox Check(TableLayoutPanel page, string title, bool value)
        {
            var control = new CheckBox { Text = title, Checked = value, AutoSize = true, ForeColor = Theme.Text,
                Font = Theme.Body, Padding = new Padding(0, 3, 0, 3) };
            Add(page, control); return control;
        }
        NumericUpDown Number(TableLayoutPanel page, string title, int value, int min, int max)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
            var label = Theme.Label(title); label.Margin = new Padding(0, 6, 12, 0); label.AutoSize = false; label.Width = 325;
            var number = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 88,
                BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body, AccessibleName = title };
            row.Controls.Add(label); row.Controls.Add(number); Add(page, row); return number;
        }

        var historyPage = Page("History");
        Section(historyPage, "RECENT CLIPS");
        menuCount = Number(historyPage, "Clips in the quick menu", settings.MenuCount, 1, 30);
        historyLimit = Number(historyPage, "Maximum unpinned clips", settings.HistoryLimit, 10, 1000);
        save = Check(historyPage, "Keep encrypted history between sessions", settings.SaveHistory);
        images = Check(historyPage, "Capture images", settings.CaptureImages);
        files = Check(historyPage, "Capture copied file references", settings.CaptureFiles);
        Section(historyPage, "RETENTION AND PINNED CLIPS");
        retentionDays = Number(historyPage, "Expire unpinned clips after (days)", settings.RetentionDays, 0, 3650);
        Help(historyPage, "0 keeps clips without an age limit. Changes apply when you save.\nCopying a clip again restarts its age; pasting does not.");
        Help(historyPage, "Up to 50 pins / 16 MiB, within the total 48 MiB history budget.\nUnpinning restores age and count limits and may remove the clip immediately.");

        var accessPage = Page("Access");
        Section(accessPage, "RIGHT-CLICK");
        companion = Check(accessPage, "Show companion on ordinary right-click", settings.CompanionMenu);
        Help(accessPage, "Shown near the pointer only in recognized editable text fields.\nRead-only fields and unrecognized surfaces do not show it.\nThe original menu stays available.");
        explorer = Check(accessPage, "Add a desktop and Explorer context-menu command", settings.ExplorerMenu);
        Section(accessPage, "KEYBOARD SHORTCUT");
        hotkey = Check(accessPage, "Enable global shortcut", settings.HotkeyEnabled);
        var shortcutRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        CheckBox Modifier(string title, uint flag) => new() { Text = title, AutoSize = true,
            Checked = (settings.HotkeyModifiers & flag) != 0, ForeColor = Theme.Text, Margin = new Padding(0, 6, 16, 0) };
        controlKey = Modifier("Ctrl", 2); altKey = Modifier("Alt", 1); shiftKey = Modifier("Shift", 4);
        shortcutKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, BackColor = Theme.Surface,
            ForeColor = Theme.Text, AccessibleName = "Shortcut key" };
        foreach (var key in HotkeyGesture.SupportedKeys) shortcutKey.Items.Add(new KeyChoice(key));
        shortcutKey.SelectedItem = shortcutKey.Items.Cast<KeyChoice>().FirstOrDefault(key => key.Code == settings.HotkeyKey);
        if (shortcutKey.SelectedIndex < 0) shortcutKey.SelectedItem = shortcutKey.Items.Cast<KeyChoice>().Single(key => key.Code == HotkeyGesture.Default.Key);
        shortcutRow.Controls.AddRange([controlKey, altKey, shiftKey, shortcutKey]); Add(accessPage, shortcutRow);
        Help(accessPage, "Choose Ctrl and/or Alt; Shift is optional. F12 and Windows-key\nshortcuts are reserved. A global shortcut can override an app's own action.");
        Section(accessPage, "STARTUP");
        startup = Check(accessPage, "Start when I sign in to Windows", settings.StartWithWindows);

        var privacyPage = Page("Privacy");
        Section(privacyPage, "SOURCE INFORMATION");
        rememberSource = Check(privacyPage, "Remember the source app for new clips", settings.RememberSourceApp);
        Help(privacyPage, "Save the clipboard owner's executable name for search and filtering.\nWindow titles and website addresses are not collected. Turning this off\nstops future collection; source names already in history are kept.");
        Section(privacyPage, "EXCLUDED APPLICATIONS");
        Help(privacyPage, "Do not capture from these processes (one executable name per line).\nExamples: KeePass.exe, KeePassXC.exe, 1Password.exe. No paths or wildcards.");
        excluded = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Top, Height = 170, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body,
            AccessibleName = "Excluded applications", Text = string.Join(Environment.NewLine, settings.ExcludedProcesses ?? []) };
        Add(privacyPage, excluded);
        Help(privacyPage, "Matches the clipboard owner or the active app when a copy is detected.\nWith a non-empty list, unidentified sources are skipped before reading content.");
        Help(privacyPage, "Applies to future copies. Existing history is kept. Excluding a browser\naffects all its sites; browser extensions and helper processes may use other names.");
        Help(privacyPage, "Supported privacy markers are always honored. Exclusions cannot identify\nevery secret. Pause capture from the tray when you need it.");

        var aboutPage = Page("About");
        Section(aboutPage, "PASTEFROMHISTORY " + Application.ProductVersion.Split('+')[0]);
        Help(aboutPage, "Local clipboard history for Windows. MIT licensed.\nNo account, telemetry, or background update checks.");
        Help(aboutPage, "Check for updates contacts GitHub for the latest stable release.\nIt sends no clipboard content, source names, or settings.\nDownloads and installation are started by you.");
        var update = Theme.Button("Check for updates", () => { });
        update.Click += async (_, _) =>
        {
            update.Enabled = false; update.Text = "Checking…";
            try
            {
                var release = await ReleaseUpdateService.CheckAsync();
                if (IsDisposed) return;
                var current = Version.Parse(Application.ProductVersion.Split('+')[0]);
                if (release.Version > current)
                {
                    if (MessageBox.Show(this, $"Version {release.Version} is available. You are using {current}.\n\nOpen its GitHub release page?",
                        "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true });
                }
                else MessageBox.Show(this, $"You are using version {current}. No newer stable release is available.",
                    "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                or InvalidDataException or System.ComponentModel.Win32Exception)
            {
                if (!IsDisposed) MessageBox.Show(this, "The update check could not be completed. Try again later or visit the project's GitHub Releases page.",
                    "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally { if (!IsDisposed) { update.Text = "Check for updates"; update.Enabled = true; } }
        };
        Add(aboutPage, update);
        Help(aboutPage, "Release downloads include SHA-256 checksums. Current builds are\nunsigned; checksums do not establish a publisher identity.");

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        var submit = Theme.Button("Save", Save, true);
        var cancel = Theme.Button("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        actions.Controls.Add(submit); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 2);
        AcceptButton = submit; CancelButton = cancel;
        Theme.Finish(this);
    }

    private void Save()
    {
        void Invalid(string message) => MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        if (menuCount.Value > historyLimit.Value) { Invalid("The quick menu cannot contain more clips than the unpinned history limit."); return; }
        var gesture = new HotkeyGesture((controlKey.Checked ? 2u : 0) | (altKey.Checked ? 1u : 0) | (shiftKey.Checked ? 4u : 0),
            ((KeyChoice)shortcutKey.SelectedItem!).Code);
        if (hotkey.Checked && !gesture.IsValid) { Invalid("Choose at least Ctrl or Alt for the global shortcut."); return; }
        if (!CaptureExclusions.TryParse(excluded.Text, out var names, out var error)) { Invalid(error!); return; }
        var candidate = original with
        {
            MenuCount = (int)menuCount.Value, HistoryLimit = (int)historyLimit.Value, RetentionDays = (int)retentionDays.Value,
            SaveHistory = save.Checked, CaptureImages = images.Checked, CaptureFiles = files.Checked,
            CompanionMenu = companion.Checked, ExplorerMenu = explorer.Checked,
            HotkeyEnabled = hotkey.Checked, HotkeyModifiers = gesture.Modifiers, HotkeyKey = gesture.Key,
            ExcludedProcesses = names, RememberSourceApp = rememberSource.Checked,
            StartWithWindows = startup.Checked, SetupComplete = true
        };
        if (apply?.Invoke(candidate) is { } failure) { Invalid(failure); return; }
        Result = candidate;
        DialogResult = DialogResult.OK; Close();
    }
}
