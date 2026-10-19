using System.Drawing.Drawing2D;
using PasteFromHistory.Services;

namespace PasteFromHistory.UI;

internal sealed class MenuCompanion : Form
{
    private readonly System.Windows.Forms.Timer lifetime = new() { Interval = 120 };
    private nint target;
    private PasteTarget pasteTarget = new(0);
    private nint nativeMenu;
    private DateTime shownAt;
    private bool hovering;
    public event Action<PasteTarget, Point>? OpenHistory;

    public MenuCompanion()
    {
        Theme.Style(this);
        Text = "Paste from history · companion";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(240, 46); Cursor = Cursors.Hand; DoubleBuffered = true;
        AccessibleName = "Paste from history";
        lifetime.Tick += (_, _) => CheckVisibility();
        MouseEnter += (_, _) => { hovering = true; Invalidate(); };
        MouseLeave += (_, _) => { hovering = false; Invalidate(); };
        MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var destination = pasteTarget;
            var point = Location;
            Hide(); lifetime.Stop();
            OpenHistory?.Invoke(destination, point);
        };
        Theme.Finish(this);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var value = base.CreateParams;
            // Form.TopMost makes SetVisibleCore call FocusActiveControlInternal,
            // even with ShowWithoutActivation, and closes the other app's menu.
            // Set the native topmost style without enabling that WinForms path.
            value.ExStyle |= 0x08000000 | 0x00000008; // WS_EX_NOACTIVATE | WS_EX_TOPMOST
            if (!ShowInTaskbar) value.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return value;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x21) { message.Result = 3; return; } // MA_NOACTIVATE
        base.WndProc(ref message);
    }

    public void ShowBeside(Point click, PasteTarget destination)
    {
        var window = destination.Window;
        target = window;
        pasteTarget = destination;
        var menu = Native.FindMenu(window, click);
        nativeMenu = menu?.Handle ?? 0;
        var work = Screen.FromPoint(click).WorkingArea;
        // Move the hidden handle onto the target monitor first, so WinForms applies
        // its DPI before calculating the final bounds. Screen coordinates are pixels.
        _ = Handle;
        Location = click;
        Location = CompanionPlacement.Locate(click, Size, work, menu?.Bounds, Math.Max(1, DeviceDpi * 6 / 96));
        shownAt = DateTime.UtcNow;
        hovering = false;
        // A foreign owner would couple our input queue/lifetime to another app.
        Show(); lifetime.Start();
    }

    private void CheckVisibility()
    {
        if (!Visible) { lifetime.Stop(); return; }
        // The native menu can close on mouse-down before our mouse-up arrives.
        // Keep the companion alive for the whole click, including a held button.
        if ((Native.GetAsyncKeyState(1) & 0x8000) != 0 && Bounds.Contains(Cursor.Position)) return;
        var foreground = Native.Root(Native.GetForegroundWindow());
        // Accessibility tools can explicitly activate a no-activate window. Do
        // not remove the button between that activation and its click.
        var companionActive = foreground == Handle;
        if (!Native.IsWindow(target) || (!companionActive && foreground != Native.Root(target))
            || (Native.GetAsyncKeyState(Native.VK_ESCAPE) & 0x8000) != 0
            || (nativeMenu == 0 && (DateTime.UtcNow - shownAt).TotalSeconds > 60)
            || (!companionActive && nativeMenu != 0 && !Native.IsWindowVisible(nativeMenu)))
        { Hide(); lifetime.Stop(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(hovering ? Theme.Hover : Theme.Surface);
        using var border = new Pen(Theme.Accent, 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var accent = new SolidBrush(Theme.Accent);
        e.Graphics.FillEllipse(accent, 14 * scale, 16 * scale, 12 * scale, 12 * scale);
        TextRenderer.DrawText(e.Graphics, "Paste from history", Theme.Body,
            new Rectangle((int)(38 * scale), 0, Width - (int)(62 * scale), Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, "›", Theme.Body, new Rectangle(Width - (int)(26 * scale), 0, (int)(22 * scale), Height),
            Theme.Accent, TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing) { if (disposing) lifetime.Dispose(); base.Dispose(disposing); }
}
