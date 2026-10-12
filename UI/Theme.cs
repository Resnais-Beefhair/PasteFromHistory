using System.Drawing.Drawing2D;
using PasteFromHistory.Services;

namespace PasteFromHistory.UI;

internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(17, 25, 27);
    public static readonly Color Surface = Color.FromArgb(26, 37, 40);
    public static readonly Color Hover = Color.FromArgb(37, 55, 58);
    public static readonly Color Border = Color.FromArgb(51, 70, 73);
    public static readonly Color Text = Color.FromArgb(237, 245, 242);
    public static readonly Color Muted = Color.FromArgb(147, 168, 162);
    public static readonly Color Accent = Color.FromArgb(166, 232, 202);
    public static readonly Font Body = new("Segoe UI", 10);
    public static readonly Font Small = new("Segoe UI", 9);
    public static readonly Font Title = new("Segoe UI Semibold", 23);

    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 6, 12, 6), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface, ForeColor = primary ? Back : Text,
            Font = Body, Cursor = Cursors.Hand, Margin = new Padding(5, 3, 0, 3),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(194, 247, 222) : Hover;
        button.Click += (_, _) => action();
        return button;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, Font = font ?? Body, ForeColor = color ?? Text,
        BackColor = Color.Transparent, AutoSize = true, UseMnemonic = false
    };

    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Accent);
        using var outline = new Pen(Back, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.FillEllipse(background, 2, 2, 60, 60);
        graphics.DrawRectangle(outline, 21, 18, 24, 33);
        using var header = new SolidBrush(Back);
        graphics.FillRectangle(header, 26, 13, 14, 10);
        graphics.DrawLine(outline, 28, 30, 39, 30);
        graphics.DrawLine(outline, 28, 38, 36, 38);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    public static void Style(Form form)
    {
        form.SuspendLayout();
        form.BackColor = Back; form.ForeColor = Text; form.Font = Body;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Icon = CreateIcon();
    }

    public static void Finish(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.ResumeLayout(true);
    }
}
