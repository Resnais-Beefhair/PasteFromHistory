using System.ComponentModel;
using System.Diagnostics;

namespace PasteFromHistory.Services;

internal readonly record struct ClipboardSource(uint Sequence, string? Owner, string? Foreground)
{
    internal static ClipboardSource Capture() => new(Native.GetClipboardSequenceNumber(),
        ProcessName(Native.GetClipboardOwner()), ProcessName(Native.GetForegroundWindow()));

    internal static string? ProcessName(nint window)
    {
        if (window == 0) return null;
        Native.GetWindowThreadProcessId(window, out var id);
        if (id == 0 || id > int.MaxValue) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        { return null; }
    }
}
