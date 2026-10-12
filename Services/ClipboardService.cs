using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PasteFromHistory.Core;

namespace PasteFromHistory.Services;

public static class ClipboardCodec
{
    public const string OwnFormat = Compatibility.LegacyName + ".Internal.1";

    public static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent(OwnFormat, false)
            || data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing", false)
            || data.GetDataPresent("application/x-keepassxc-confidential", false)
            || data.GetDataPresent("Clipboard Viewer Ignore", false)) return true;
        if (!data.GetDataPresent("CanIncludeInClipboardHistory", false)) return false;
        var marker = data.GetData("CanIncludeInClipboardHistory", false);
        return marker switch
        {
            MemoryStream stream => stream.Length < 4 || BitConverter.ToUInt32(stream.ToArray(), 0) == 0,
            byte[] bytes => bytes.Length < 4 || BitConverter.ToUInt32(bytes, 0) == 0,
            int value => value == 0,
            uint value => value == 0,
            _ => true // Unknown opt-out representation is treated conservatively.
        };
    }

    public static ClipEntry? Read(IDataObject data, AppSettings settings)
    {
        if (IsPrivate(data)) return null;
        ClipEntry? entry = null;
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            if (!settings.CaptureFiles) return null;
            if (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                entry = new() { Kind = ClipKind.Files, Paths = paths.ToArray() };
        }
        else if (data.GetDataPresent("PNG", false) || data.GetDataPresent(DataFormats.Bitmap))
        {
            if (!settings.CaptureImages) return null;
            byte[]? png = data.GetData("PNG", false) switch
            {
                MemoryStream stream when stream.Length <= History.MaxEntryBytes => stream.ToArray(),
                byte[] bytes when bytes.Length <= History.MaxEntryBytes => bytes,
                _ => null
            };
            if (png != null)
            {
                if (!PngHeader.TryReadSize(png, out var width, out var height)
                    || (long)width * height > History.MaxPixels) return null;
                using var stream = new MemoryStream(png);
                using var image = System.Drawing.Image.FromStream(stream);
                if (image.Width != width || image.Height != height) return null;
                entry = new() { Kind = ClipKind.Image, Png = png, Width = image.Width, Height = image.Height };
            }
            else if (data.GetData(DataFormats.Bitmap) is System.Drawing.Image image)
            {
                using (image)
                using (var stream = new MemoryStream())
                {
                    if ((long)image.Width * image.Height > History.MaxPixels) return null;
                    image.Save(stream, ImageFormat.Png);
                    entry = new() { Kind = ClipKind.Image, Png = stream.ToArray(), Width = image.Width, Height = image.Height };
                }
            }
        }
        else if (data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrEmpty(text))
        {
            entry = new()
            {
                Kind = ClipKind.Text, Text = text,
                Html = data.GetData(DataFormats.Html, false) as string,
                Rtf = data.GetData(DataFormats.Rtf, false) as string
            };
        }
        return entry != null && History.IsValid(entry) ? entry : null;
    }

    public static DataObject Encode(ClipEntry entry)
    {
        var data = new DataObject();
        data.SetData(OwnFormat, false, new MemoryStream([1]));
        switch (entry.Kind)
        {
            case ClipKind.Text:
                if (entry.Html != null) data.SetData(DataFormats.Html, false, entry.Html);
                if (entry.Rtf != null) data.SetData(DataFormats.Rtf, false, entry.Rtf);
                data.SetData(DataFormats.UnicodeText, true, entry.Text);
                break;
            case ClipKind.Image:
                if (!History.IsValid(entry)) throw new InvalidDataException("The image has invalid or excessive dimensions.");
                using (var stream = new MemoryStream(entry.Png!))
                using (var image = System.Drawing.Image.FromStream(stream))
                    data.SetData(DataFormats.Bitmap, true, new Bitmap(image));
                data.SetData("PNG", false, new MemoryStream(entry.Png!));
                break;
            case ClipKind.Files:
                var paths = new StringCollection(); paths.AddRange(entry.Paths);
                data.SetFileDropList(paths);
                // History always copies files; it never repeats an old Cut operation.
                data.SetData("Preferred DropEffect", false, new MemoryStream(BitConverter.GetBytes(1)));
                break;
        }
        return data;
    }

    public static void Write(ClipEntry entry)
    {
        var data = Encode(entry);
        try { Clipboard.SetDataObject(data, true, 8, 50); }
        finally
        {
            if (entry.Kind == ClipKind.Image && data.TryGetData<Bitmap>(DataFormats.Bitmap, out var image)) image.Dispose();
        }
    }
}

internal sealed class MessageWindow : Form
{
    private readonly System.Windows.Forms.Timer retry = new() { Interval = 90 };
    private int attempts;
    private bool registered;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<AppSettings> Settings { get; set; } = () => new();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Paused { get; set; }
    public event Action<ClipEntry>? Captured;
    public event Action? Hotkey;

    public MessageWindow()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        retry.Tick += (_, _) => ReadPending();
        _ = Handle;
    }

    public void StartListening()
    {
        registered = Native.AddClipboardFormatListener(Handle);
        if (!registered) throw new InvalidOperationException("Clipboard monitoring could not start.");
    }

    public bool SetHotkey(bool enabled)
    {
        Native.UnregisterHotKey(Handle, 1);
        return !enabled || Native.RegisterHotKey(Handle, 1, 0x4006, 0x56); // Ctrl + Shift + V, no repeat
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == Native.WM_CLIPBOARDUPDATE)
        {
            retry.Stop(); attempts = 0;
            if (!Paused) retry.Start();
        }
        else if (message.Msg == Native.WM_HOTKEY) Hotkey?.Invoke();
        base.WndProc(ref message);
    }

    private void ReadPending()
    {
        retry.Stop();
        if (Paused) return;
        try
        {
            var sequence = Native.GetClipboardSequenceNumber();
            var data = Clipboard.GetDataObject();
            var entry = data == null ? null : ClipboardCodec.Read(data, Settings());
            if (sequence != Native.GetClipboardSequenceNumber()) { retry.Start(); return; }
            if (entry != null) Captured?.Invoke(entry);
        }
        catch (ExternalException) { if (++attempts < 8) retry.Start(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException or NotSupportedException)
        { /* Unsupported/malformed application formats are skipped without logging contents. */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            retry.Dispose();
            if (registered) Native.RemoveClipboardFormatListener(Handle);
            Native.UnregisterHotKey(Handle, 1);
        }
        base.Dispose(disposing);
    }
}
