using System.Text;
using PasteFromHistory.Core;
using Microsoft.VisualBasic.FileIO;

namespace PasteFromHistory.Services;

internal sealed record PasteTarget(nint Window, string? Folder = null, nint Focus = 0)
{
    public bool IsFolder => Folder != null;
    public static PasteTarget Capture(nint window) => new(window, Focus: Native.FocusedWindow(window));
}

internal static class PasteService
{
    public static async Task<string?> Paste(ClipEntry entry, PasteTarget target)
    {
        if (target.Folder != null) return await PasteToFolder(entry, target.Folder);
        ClipboardCodec.Write(entry);
        if (target.Window == 0 || !Native.IsWindow(target.Window) || Native.IsOwnWindow(target.Window))
            return "Copied to the clipboard. Focus the destination and press Ctrl+V.";

        Native.SetForegroundWindow(target.Window);
        for (var i = 0; i < 30 && Native.ModifiersPressed(); i++) await Task.Delay(50);
        await Task.Delay(90);
        if (Native.ModifiersPressed() || Native.Root(Native.GetForegroundWindow()) != Native.Root(target.Window))
            return "The content is on the clipboard. Focus the destination and press Ctrl+V.";
        // XAML menus can leave focus on their input site after dismissal. Restore
        // the control that was focused before right-button-down, preserving its selection.
        if (target.Focus != 0 && !Native.RestoreFocus(target.Window, target.Focus))
            return "The content is on the clipboard. Focus the text field and press Ctrl+V.";
        return Native.SendPaste() ? null : "Automatic paste was blocked. The content is on the clipboard; press Ctrl+V to paste.";
    }

    private static async Task<string?> PasteToFolder(ClipEntry entry, string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("The destination folder no longer exists.");
        if (entry.Kind != ClipKind.Files)
        {
            var extension = entry.Kind == ClipKind.Image ? ".png" : ".txt";
            var stem = "Clip " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            var bytes = entry.Kind == ClipKind.Image ? entry.Png! : Encoding.UTF8.GetBytes(entry.Text);
            await Task.Run(() => WriteUnique(folder, stem, extension, file => file.Write(bytes)));
            return entry.Kind == ClipKind.Image ? "Image saved to the destination folder as a PNG file." : "Text saved to the destination folder as a TXT file.";
        }

        var missing = entry.Paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToArray();
        if (missing.Length != 0) throw new IOException("One or more original files could not be found. Nothing was copied.");

        // Shell copy dialogs provide normal progress, overwrite prompts and cancellation.
        await RunSta(() =>
        {
            foreach (var source in entry.Paths)
            {
                var isDirectory = Directory.Exists(source);
                var destination = Path.Combine(folder, Path.GetFileName(source.TrimEnd('\\', '/')));
                if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), destination.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    destination = AvailableCopyName(destination, isDirectory);
                if (isDirectory)
                {
                    var prefix = Path.GetFullPath(source).TrimEnd('\\') + "\\";
                    if (destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("A folder cannot be copied into itself.");
                    FileSystem.CopyDirectory(source, destination, UIOption.AllDialogs, UICancelOption.ThrowException);
                }
                else FileSystem.CopyFile(source, destination, UIOption.AllDialogs, UICancelOption.ThrowException);
            }
        });
        return "Files copied to the destination folder.";
    }

    internal static string AvailableCopyName(string path, bool directory)
    {
        var parent = Path.GetDirectoryName(path)!;
        var name = directory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var extension = directory ? "" : Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(parent, $"{name} - copy ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    internal static string WriteUnique(string folder, string stem, string extension, Action<Stream> write)
    {
        for (var suffix = 0; ; suffix++)
        {
            var path = Path.Combine(folder, stem + (suffix == 0 ? "" : $" ({suffix})") + extension);
            FileStream file;
            try { file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try { using (file) write(file); }
            catch
            {
                // A write failure (e.g. full disk) is not a filename collision.
                // Remove only the partial file just created by this operation.
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
            return path;
        }
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "PasteFromHistory.FileCopy" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
