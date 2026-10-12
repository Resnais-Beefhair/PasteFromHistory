using PasteFromHistory.Core;

namespace PasteFromHistory.UI;

internal sealed class HistoryForm : Form
{
    private readonly History history;
    private readonly AppSettings settings;
    private readonly bool compact;
    private readonly TextBox search;
    private readonly ListBox list;
    private readonly Label count;
    private readonly Label empty;
    private readonly TextBox preview;
    private readonly PictureBox picture;
    private readonly Label previewTitle;
    private readonly Label previewMeta;
    private readonly Button paste;
    private readonly Button copy;
    private readonly Button remove;
    private readonly Button pastePlain;
    private readonly Button pin;
    private readonly Dictionary<Guid, Image> thumbnails = [];
    private readonly List<Button> filterButtons = [];
    private ClipKind? filter;
    private bool pinnedOnly;
    public event Action<ClipEntry, bool, bool>? Chosen;
    public event Action? SettingsRequested;
    public event Action? FullHistoryRequested;
    public event Action? ClearRequested;

    public HistoryForm(History history, AppSettings settings, bool compact, string? folder = null)
    {
        this.history = history; this.settings = settings; this.compact = compact;
        Theme.Style(this);
        Text = compact ? "Paste from history" : "PasteFromHistory";
        ClientSize = compact ? new Size(480, Math.Clamp(320 + settings.MenuCount * 51, 420, 1100)) : new Size(940, 680);
        MinimumSize = compact ? new Size(420, 390) : new Size(780, 560);
        StartPosition = compact ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
        FormBorderStyle = compact ? FormBorderStyle.FixedSingle : FormBorderStyle.Sizable;
        ShowInTaskbar = !compact; MaximizeBox = !compact; MinimizeBox = !compact;
        TopMost = compact; KeyPreview = true;
        Padding = new Padding(compact ? 16 : 24);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Theme.Back };
        foreach (var size in new[] { 70f, 43f, 40f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, size));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, compact ? 96 : 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(layout);

        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = Theme.Label(compact ? "Paste from history" : "PasteFromHistory", Theme.Title);
        title.Location = new Point(0, 0); header.Controls.Add(title);
        var subtitle = Theme.Label(compact ? "Pinned first, then your recent clips." : "Everything you copied, close at hand.", Theme.Small, Theme.Muted);
        subtitle.Location = new Point(2, 43); header.Controls.Add(subtitle);
        if (!compact)
        {
            var settingsButton = Theme.Button("Settings", () => SettingsRequested?.Invoke());
            settingsButton.Dock = DockStyle.Right; settingsButton.AutoSize = false; settingsButton.Size = new Size(112, 40);
            var settingsHost = new Panel { Dock = DockStyle.Right, Width = 115, Padding = new Padding(0, 8, 0, 20) };
            settingsHost.Controls.Add(settingsButton); header.Controls.Add(settingsHost);
        }
        layout.Controls.Add(header, 0, 0);

        search = new TextBox
        {
            Dock = DockStyle.Top, PlaceholderText = "Search history…", Font = new Font("Segoe UI", 11),
            BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 0, 4), AccessibleName = "Search history"
        };
        search.TextChanged += (_, _) => RefreshEntries();
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && list!.Items.Count > 0) { list.Focus(); e.Handled = true; }
        };
        layout.Controls.Add(search, 0, 1);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        count = Theme.Label("", Theme.Small, Theme.Muted);
        count.Margin = new Padding(2, 9, 0, 0);
        if (!compact)
        {
            AddFilter("All", null); AddFilter("Text", ClipKind.Text); AddFilter("Images", ClipKind.Image); AddFilter("Files", ClipKind.Files); AddFilter("Pinned", null, true);
            foreach (var button in filterButtons) filters.Controls.Add(button);
            count.Margin = new Padding(18, 9, 0, 0);
        }
        filters.Controls.Add(count); layout.Controls.Add(filters, 0, 2);

        list = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = compact ? 51 : 68, IntegralHeight = false,
            AccessibleName = "Clipboard history", SelectionMode = SelectionMode.One
        };
        list.DrawItem += DrawEntry;
        list.SelectedIndexChanged += (_, _) => UpdatePreview();
        list.DoubleClick += (_, _) => { if (!compact) Choose(true, ModifierKeys.HasFlag(Keys.Shift)); };
        list.MouseClick += (_, e) =>
        {
            if (compact && e.Button == MouseButtons.Left && list.IndexFromPoint(e.Location) >= 0) Choose(true, ModifierKeys.HasFlag(Keys.Shift));
        };
        empty = Theme.Label("Your history starts here.\n\nCopy some text, an image, or a file.", Theme.Body, Theme.Muted);
        empty.AutoSize = false; empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter;
        empty.BackColor = Theme.Surface;
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border, Margin = Padding.Empty };
        listHost.Controls.Add(list); listHost.Controls.Add(empty);

        previewTitle = Theme.Label("PREVIEW", Theme.Small, Theme.Accent);
        previewTitle.Dock = DockStyle.Top; previewTitle.Height = 30; previewTitle.AutoSize = false;
        previewMeta = Theme.Label("", Theme.Small, Theme.Muted);
        previewMeta.Dock = DockStyle.Bottom; previewMeta.Height = 44; previewMeta.AutoSize = false;
        preview = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Body, AccessibleName = "Clip preview" };
        picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface, Visible = false };
        var previewHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(18), Margin = Padding.Empty };
        previewHost.Controls.Add(preview); previewHost.Controls.Add(picture); previewHost.Controls.Add(previewTitle); previewHost.Controls.Add(previewMeta);
        if (compact) layout.Controls.Add(listHost, 0, 3);
        else
        {
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
            body.Controls.Add(listHost, 0, 0); body.Controls.Add(previewHost, 2, 0);
            layout.Controls.Add(body, 0, 3);
        }

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 7, 0, 0) };
        paste = Theme.Button(folder == null ? "Paste" : "Paste to folder", () => Choose(true, ModifierKeys.HasFlag(Keys.Shift)), true);
        pastePlain = Theme.Button("Paste as text", () => Choose(true, true));
        pin = Theme.Button("Pin", TogglePin);
        copy = Theme.Button("Copy", () => Choose(false));
        remove = Theme.Button("Delete", () => { if (Selected is { } entry) history.Remove(entry.Id); });
        actions.Controls.Add(paste); actions.Controls.Add(pastePlain); actions.Controls.Add(copy);
        if (compact) actions.Controls.Add(Theme.Button("Full history", () => FullHistoryRequested?.Invoke()));
        else
        {
            actions.Controls.Add(pin);
            actions.Controls.Add(remove);
            actions.Controls.Add(Theme.Button("Clear", () => ClearRequested?.Invoke()));
        }
        layout.Controls.Add(actions, 0, 4);
        var tip = Theme.Label(folder != null ? "To folder: " + folder : compact ? "Shift+Enter / Shift+click: plain text\nEnter: paste    ·    Esc: close    ·    Alt+1–9: select"
            : "Shift+Enter / Shift+double-click: plain text    ·    Ctrl+P: pin or unpin\nRight-click → Paste from history"
                + (settings.HotkeyEnabled ? "    ·    " + settings.Shortcut : ""), Theme.Small, Theme.Muted);
        tip.AutoSize = false; tip.Dock = DockStyle.Fill; tip.AutoEllipsis = true; tip.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(tip, 0, 5);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter && !e.Control) { Choose(true, e.Shift); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.F) { search.Focus(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.P) { TogglePin(); e.SuppressKeyPress = true; }
            else if (e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                var index = (int)e.KeyCode - (int)Keys.D1;
                if (index < list.Items.Count) { list.SelectedIndex = index; Choose(true); }
                e.SuppressKeyPress = true;
            }
        };
        if (compact) Deactivate += (_, _) => { if (!IsDisposed) Close(); };
        history.Changed += RefreshEntries;
        FormClosed += (_, _) => history.Changed -= RefreshEntries;
        Shown += (_, _) => search.Focus();
        RefreshEntries();
        Theme.Finish(this);
        list.HandleCreated += (_, _) => list.ItemHeight = (int)((compact ? 51 : 68) * DeviceDpi / 96f);
    }

    private ClipEntry? Selected => list.SelectedItem as ClipEntry;

    private void AddFilter(string text, ClipKind? kind, bool onlyPins = false)
    {
        var button = Theme.Button(text, () => { filter = kind; pinnedOnly = onlyPins; RefreshEntries(); });
        button.Tag = (kind, onlyPins); button.Font = Theme.Small; button.Padding = new Padding(9, 2, 9, 2); button.Margin = new Padding(0, 0, 6, 0);
        filterButtons.Add(button);
    }

    private void Choose(bool shouldPaste, bool plainText = false)
    {
        if (Selected is { } entry) Chosen?.Invoke(entry, shouldPaste, plainText && entry.Kind == ClipKind.Text);
    }

    private void TogglePin()
    {
        if (Selected is not { } entry) return;
        var error = history.SetPinned(entry.Id, !entry.Pinned, settings.HistoryLimit, settings.RetentionDays);
        if (error != null) MessageBox.Show(this, error, "Pinned clips", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void RefreshEntries()
    {
        if (IsDisposed) return;
        var id = Selected?.Id;
        var entries = history.Entries.Where(e => (filter == null || filter == e.Kind) && (!pinnedOnly || e.Pinned) && e.Matches(search.Text))
            .OrderByDescending(e => e.Pinned).ToList();
        if (compact) entries = entries.Take(settings.MenuCount).ToList();
        list.BeginUpdate(); list.Items.Clear();
        foreach (var entry in entries) list.Items.Add(entry);
        if (entries.Count > 0) list.SelectedIndex = Math.Max(0, entries.FindIndex(e => e.Id == id));
        list.EndUpdate();
        count.Text = compact ? $"{entries.Count} shown · {history.Entries.Count} total" : $"{entries.Count} clips · {history.Entries.Count(e => e.Pinned)} pinned";
        empty.Text = string.IsNullOrEmpty(search.Text) && filter == null && !pinnedOnly
            ? "Your history starts here.\n\nCopy some text, an image, or a file." : "No matches.\n\nTry another search.";
        empty.Visible = entries.Count == 0;
        foreach (var button in filterButtons)
        {
            var active = Equals(button.Tag, (filter, pinnedOnly));
            button.BackColor = active ? Theme.Hover : Theme.Back;
            button.ForeColor = active ? Theme.Accent : Theme.Muted;
        }
        UpdatePreview();
        var retainedIds = history.Entries.Select(entry => entry.Id).ToHashSet();
        foreach (var idToRemove in thumbnails.Keys.Where(key => !retainedIds.Contains(key)).ToArray())
        {
            thumbnails[idToRemove].Dispose();
            thumbnails.Remove(idToRemove);
        }
    }

    private void UpdatePreview()
    {
        var entry = Selected;
        paste.Enabled = copy.Enabled = remove.Enabled = pin.Enabled = entry != null;
        pastePlain.Enabled = entry?.Kind == ClipKind.Text;
        pin.Text = entry?.Pinned == true ? "Unpin" : "Pin";
        picture.Image = null; picture.Visible = false; preview.Visible = true;
        preview.Text = "Select a clip to preview its contents.";
        previewTitle.Text = "PREVIEW"; previewMeta.Text = "";
        if (entry == null) return;
        previewTitle.Text = entry.KindLabel + (entry.Pinned ? " · PINNED" : "");
        previewMeta.Text = entry.CapturedAt.ToLocalTime().ToString("g") + "\n" + entry.Detail;
        preview.Text = entry.Kind == ClipKind.Files ? string.Join(Environment.NewLine + Environment.NewLine, entry.Paths) : entry.Text;
        if (entry.Kind == ClipKind.Image)
        {
            picture.Image = GetThumbnail(entry); picture.Visible = true; preview.Visible = false;
        }
    }

    private Image? GetThumbnail(ClipEntry entry)
    {
        if (thumbnails.TryGetValue(entry.Id, out var value)) return value;
        try
        {
            if (!History.IsValid(entry)) return null;
            using var stream = new MemoryStream(entry.Png!);
            using var image = Image.FromStream(stream);
            var ratio = Math.Min(1d, 700d / Math.Max(image.Width, image.Height));
            var thumbnail = new Bitmap(image, new Size(Math.Max(1, (int)(image.Width * ratio)), Math.Max(1, (int)(image.Height * ratio))));
            thumbnails[entry.Id] = thumbnail;
            return thumbnail;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException) { return null; }
    }

    private void DrawEntry(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= list.Items.Count) return;
        var entry = (ClipEntry)list.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Theme.Hover : Theme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        var scale = DeviceDpi / 96f;
        int S(int value) => (int)(value * scale);
        if (selected) { using var accent = new SolidBrush(Theme.Accent); e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + S(9), S(3), e.Bounds.Height - S(18)); }
        var icon = new Rectangle(e.Bounds.Left + S(13), e.Bounds.Top + S(10), S(32), S(32));
        if (entry.Kind == ClipKind.Image && GetThumbnail(entry) is { } image)
        {
            var ratio = Math.Min((float)icon.Width / image.Width, (float)icon.Height / image.Height);
            var width = image.Width * ratio; var height = image.Height * ratio;
            e.Graphics.DrawImage(image, icon.X + (icon.Width - width) / 2, icon.Y + (icon.Height - height) / 2, width, height);
        }
        else
        {
            TextRenderer.DrawText(e.Graphics, compact ? (e.Index + 1).ToString("00") : entry.Kind == ClipKind.Files ? "▤" : "Aa",
                Theme.Body, icon, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var text = new Rectangle(e.Bounds.Left + S(58), e.Bounds.Top + S(8), e.Bounds.Width - S(74), S(23));
        TextRenderer.DrawText(e.Graphics, entry.Title, Theme.Body, text, Theme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var meta = new Rectangle(text.Left, text.Top + S(22), text.Width, S(20));
        TextRenderer.DrawText(e.Graphics, (entry.Pinned ? "PINNED  ·  " : "") + entry.KindLabel + "  ·  " + entry.CapturedAt.ToLocalTime().ToString("HH:mm") + (compact ? "" : "  ·  " + entry.Detail),
            Theme.Small, meta, Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        using var line = new Pen(Theme.Border);
        e.Graphics.DrawLine(line, e.Bounds.Left + S(14), e.Bounds.Bottom - 1, e.Bounds.Right - S(14), e.Bounds.Bottom - 1);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        list.ItemHeight = (int)((compact ? 51 : 68) * DeviceDpi / 96f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { history.Changed -= RefreshEntries; foreach (var image in thumbnails.Values) image.Dispose(); }
        base.Dispose(disposing);
    }
}
