using PasteFromHistory.Core;

namespace PasteFromHistory.UI;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown menuCount;
    private readonly NumericUpDown historyLimit;
    private readonly CheckBox companion;
    private readonly CheckBox explorer;
    private readonly CheckBox hotkey;
    private readonly CheckBox startup;
    private readonly CheckBox save;
    private readonly CheckBox images;
    private readonly CheckBox files;
    private readonly AppSettings original;
    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        original = settings; Result = settings with { };
        Theme.Style(this);
        Text = "PasteFromHistory settings"; ClientSize = new Size(650, 710);
        MinimumSize = new Size(590, 550); StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; Padding = new Padding(24);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);
        var header = Theme.Label("Make it yours", Theme.Title); header.Dock = DockStyle.Fill;
        root.Controls.Add(header, 0, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        root.Controls.Add(scroll, 0, 1);
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 16, 12) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(content);

        void Add(Control control) { control.Margin = new Padding(0, 5, 0, 5); content.Controls.Add(control); }
        void Section(string title)
        {
            var label = Theme.Label(title, Theme.Small, Theme.Accent); label.Margin = new Padding(0, 17, 0, 6); content.Controls.Add(label);
        }
        CheckBox Check(string title, bool value)
        {
            var control = new CheckBox { Text = title, Checked = value, AutoSize = true, ForeColor = Theme.Text, Font = Theme.Body, Padding = new Padding(0, 3, 0, 3) };
            Add(control); return control;
        }
        NumericUpDown Number(string title, int value, int min, int max)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
            var label = Theme.Label(title); label.Margin = new Padding(0, 6, 12, 0); label.AutoSize = false; label.Width = 315;
            var number = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 80, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body };
            row.Controls.Add(label); row.Controls.Add(number); Add(row); return number;
        }

        Section("HISTORY");
        menuCount = Number("Clips in the quick menu", settings.MenuCount, 1, 30);
        historyLimit = Number("Maximum history size", settings.HistoryLimit, 10, 1000);
        save = Check("Keep encrypted history between sessions", settings.SaveHistory);
        images = Check("Capture images", settings.CaptureImages);
        files = Check("Capture copied file references", settings.CaptureFiles);
        Section("OPEN HISTORY");
        companion = Check("Show companion on ordinary right-click", settings.CompanionMenu);
        var help = Theme.Label("The button appears beside the app's menu. Placement\ncan vary between apps. The original menu stays available.", Theme.Small, Theme.Muted);
        Add(help);
        explorer = Check("Add a desktop and Explorer context-menu command", settings.ExplorerMenu);
        hotkey = Check("Open the quick menu with Ctrl+Shift+V", settings.HotkeyEnabled);
        Section("STARTUP");
        startup = Check("Start when I sign in to Windows", settings.StartWithWindows);
        var privacy = Theme.Label("History stays on this PC. Supported privacy markers are\nhonored. You can pause capture from the tray icon.", Theme.Small, Theme.Muted);
        Add(privacy);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        var submit = Theme.Button("Save", Save, true);
        var cancel = Theme.Button("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        actions.Controls.Add(submit); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 2);
        AcceptButton = submit; CancelButton = cancel;
        Theme.Finish(this);
    }

    private void Save()
    {
        if (menuCount.Value > historyLimit.Value)
        {
            MessageBox.Show(this, "The quick menu cannot contain more clips than the history limit.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Result = original with
        {
            MenuCount = (int)menuCount.Value, HistoryLimit = (int)historyLimit.Value,
            SaveHistory = save.Checked, CaptureImages = images.Checked, CaptureFiles = files.Checked,
            CompanionMenu = companion.Checked, ExplorerMenu = explorer.Checked,
            HotkeyEnabled = hotkey.Checked, StartWithWindows = startup.Checked, SetupComplete = true
        };
        DialogResult = DialogResult.OK; Close();
    }
}
