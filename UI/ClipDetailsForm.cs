using PasteFromHistory.Core;

namespace PasteFromHistory.UI;

internal sealed class ClipDetailsForm : Form
{
    private readonly TextBox name;
    private readonly ComboBox collection;
    public string ClipName => name.Text.Trim();
    public string Collection => collection.Text.Trim();

    public ClipDetailsForm(ClipEntry entry, IEnumerable<string> collections)
    {
        Theme.Style(this);
        Text = "Name and collection";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 300); MinimumSize = new Size(440, 340);
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        Padding = new Padding(20);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        foreach (var height in new[] { 26f, 40f, 26f, 40f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(layout);
        layout.Controls.Add(Theme.Label("Name", Theme.Small, Theme.Muted), 0, 0);
        name = new TextBox
        {
            Text = entry.Name, MaxLength = 120, Dock = DockStyle.Top, AccessibleName = "Clip name",
            PlaceholderText = "Optional name for this clip", BackColor = Theme.Surface, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Margin = Padding.Empty
        };
        layout.Controls.Add(name, 0, 1);
        layout.Controls.Add(Theme.Label("Collection", Theme.Small, Theme.Muted), 0, 2);
        collection = new ComboBox
        {
            Text = entry.Collection, MaxLength = 60, Dock = DockStyle.Top, AccessibleName = "Clip collection",
            DropDownStyle = ComboBoxStyle.DropDown, BackColor = Theme.Surface, ForeColor = Theme.Text,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems,
            Margin = Padding.Empty, IntegralHeight = false, DropDownHeight = 200
        };
        collection.Items.AddRange(collections.Where(value => value.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).Cast<object>().ToArray());
        collection.Text = entry.Collection;
        layout.Controls.Add(collection, 0, 3);
        var note = Theme.Label("Leave either field empty to remove it. Pin the clip to keep it as a favorite. Its copied contents stay unchanged.", Theme.Small, Theme.Muted);
        note.AutoSize = false; note.Dock = DockStyle.Fill; note.Margin = Padding.Empty;
        layout.Controls.Add(note, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var save = Theme.Button("Save", () => { DialogResult = DialogResult.OK; Close(); }, true);
        var cancel = Theme.Button("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        actions.Controls.Add(save); actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 5);
        AcceptButton = save; CancelButton = cancel;
        Shown += (_, _) => { name.Focus(); name.SelectAll(); };
        Theme.Finish(this);
    }
}
