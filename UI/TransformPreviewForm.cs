using System.Text.RegularExpressions;
using PasteFromHistory.Core;

namespace PasteFromHistory.UI;

internal sealed class TransformPreviewForm : Form
{
    private readonly ClipEntry source;
    private readonly ComboBox action;
    private readonly TextBox resultText;
    private readonly Label help, status;
    private readonly Button submit, reset;
    private int generation;
    private bool busy;
    private bool failed;
    public ClipEntry? Result { get; private set; }

    private sealed record Choice(TextTransformKind Kind)
    {
        public override string ToString() => TextTransforms.Label(Kind);
    }

    public TransformPreviewForm(ClipEntry source)
    {
        if (source.Kind != ClipKind.Text) throw new ArgumentException("Only text clips can be transformed.", nameof(source));
        this.source = source;
        Theme.Style(this);
        Text = "Edit and transform";
        ClientSize = new Size(920, 650); MinimumSize = new Size(700, 520);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
        ShowInTaskbar = false; KeyPreview = true; Padding = new Padding(20);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        // Leave room for the button's preferred height plus margins at scaled DPI.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var heading = Theme.Label("Make this paste fit", new Font("Segoe UI Semibold", 20));
        heading.Dock = DockStyle.Fill; root.Controls.Add(heading, 0, 0);
        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var label = Theme.Label("Action"); label.Margin = new Padding(0, 8, 12, 0);
        action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 255,
            AccessibleName = "Text transformation", BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body };
        foreach (var kind in Enum.GetValues<TextTransformKind>()) action.Items.Add(new Choice(kind));
        reset = Theme.Button("Reset result", RefreshResult);
        actionRow.Controls.Add(label); actionRow.Controls.Add(action); actionRow.Controls.Add(reset);
        root.Controls.Add(actionRow, 0, 1);
        help = Theme.Label("", Theme.Small, Theme.Muted); help.AutoSize = false; help.Dock = DockStyle.Fill;
        root.Controls.Add(help, 0, 2);

        var panes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        TextBox Pane(string title, string accessibleName, bool readOnly, int column)
        {
            var container = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var paneLabel = Theme.Label(title, Theme.Small, Theme.Accent); paneLabel.Dock = DockStyle.Fill;
            var editor = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = false, ReadOnly = readOnly,
                ScrollBars = ScrollBars.Both, WordWrap = true, Dock = DockStyle.Fill, BackColor = Theme.Surface,
                ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body,
                AccessibleName = accessibleName, MaxLength = TextTransforms.MaxInputCharacters, Margin = Padding.Empty };
            container.Controls.Add(paneLabel, 0, 0); container.Controls.Add(editor, 0, 1);
            panes.Controls.Add(container, column, 0); return editor;
        }
        var original = Pane("ORIGINAL · unchanged", "Original clip text", true, 0);
        original.Text = source.Text;
        resultText = Pane("RESULT · editable", "Transformed text preview", false, 2);
        root.Controls.Add(panes, 0, 3);
        status = Theme.Label("", Theme.Small, Theme.Muted); status.AutoSize = false; status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(status, 0, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        submit = Theme.Button("Paste result", Submit, true);
        var cancel = Theme.Button("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        buttons.Controls.Add(submit); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 5);
        CancelButton = cancel;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter && submit.Enabled)
            { Submit(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        action.SelectedIndex = 0;
        resultText.Text = source.Text;
        help.Text = TextTransforms.Description(TextTransformKind.Edit) + "\nEach action starts from the original. Ctrl+Enter pastes the result.";
        resultText.TextChanged += (_, _) => UpdateStatus();
        action.SelectedIndexChanged += (_, _) => RefreshResult();
        UpdateStatus();
        Theme.Finish(this);
    }

    private async void RefreshResult()
    {
        if (action.SelectedItem is not Choice choice) return;
        if (busy) return;
        var request = ++generation;
        busy = true; failed = false; resultText.ReadOnly = true; submit.Enabled = false;
        action.Enabled = false; reset.Enabled = false;
        help.Text = TextTransforms.Description(choice.Kind) + "\nEach action starts from the original. Ctrl+Enter pastes the result.";
        status.Text = "Preparing preview…";
        try
        {
            var transformed = await Task.Run(() => TextTransforms.Apply(source.Text, source.Html, choice.Kind));
            if (IsDisposed || request != generation) return;
            resultText.Text = transformed;
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            if (IsDisposed || request != generation) return;
            failed = true;
            status.Text = ex is RegexMatchTimeoutException ? "This conversion took too long. Choose another action." : ex.Message;
        }
        finally
        {
            if (!IsDisposed && request == generation)
            {
                busy = false; resultText.ReadOnly = failed; action.Enabled = true; reset.Enabled = true;
                if (!failed) UpdateStatus();
            }
        }
    }

    private void UpdateStatus()
    {
        if (busy || failed) return;
        var valid = resultText.Text.Length is > 0 and <= TextTransforms.MaxInputCharacters;
        submit.Enabled = valid;
        status.Text = resultText.Text.Length > TextTransforms.MaxInputCharacters
            ? "The result is too long (maximum 512K characters). Shorten it or use normal paste."
            : resultText.Text.Length == 0 ? "Enter some text to paste."
            : $"{resultText.Text.Length:N0} characters · Pasted as plain text · Original stays in history";
    }

    private void Submit()
    {
        if (busy || failed || !submit.Enabled) return;
        Result = source with { Text = resultText.Text, Html = null, Rtf = null };
        DialogResult = DialogResult.OK; Close();
    }
}
