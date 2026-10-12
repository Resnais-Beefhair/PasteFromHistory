using System.Runtime.InteropServices;
using System.Text;

namespace PasteFromHistory.Services;

internal static class Native
{
    internal const int WM_CLIPBOARDUPDATE = 0x031D, WM_HOTKEY = 0x0312;
    internal const int WM_RBUTTONUP = 0x0205, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
    internal const int VK_ESCAPE = 0x1B;
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    internal delegate bool EnumWindowsProc(nint window, nint param);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AddClipboardFormatListener(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RemoveClipboardFormatListener(nint window);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, nint param);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll")] internal static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(Point p) { X = p.X; Y = p.Y; } }
    [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public readonly Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct MSLLHOOKSTRUCT { public POINT Point; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort Key, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }

    internal static string ClassName(nint window) { var text = new StringBuilder(256); GetClassName(window, text, text.Capacity); return text.ToString(); }
    internal static nint Root(nint window) => GetAncestor(window, 2);
    internal static nint FocusedWindow(nint window)
    {
        if (window == 0) return 0;
        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GUITHREADINFO { Size = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }
    internal static bool RestoreFocus(nint window, nint focus)
    {
        if (focus == 0 || !IsWindow(focus) || !IsWindowVisible(focus) || Root(focus) != Root(window)
            || Root(GetForegroundWindow()) != Root(window) || ModifiersPressed()) return false;
        if (FocusedWindow(window) == focus) return true;
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(focus, out _);
        var attach = currentThread != targetThread;
        if (attach && !AttachThreadInput(currentThread, targetThread, true)) return false;
        try
        {
            if (Root(GetForegroundWindow()) != Root(window)) return false;
            SetFocus(focus);
        }
        finally
        {
            if (attach) AttachThreadInput(currentThread, targetThread, false);
        }
        return FocusedWindow(window) == focus;
    }
    internal static bool IsOwnWindow(nint window) { GetWindowThreadProcessId(window, out var pid); return pid == Environment.ProcessId; }
    internal static bool ModifiersPressed() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    internal static INPUT Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u } } };
    internal static bool SendPaste() { var inputs = new[] { Key(0x11), Key(0x56), Key(0x56, true), Key(0x11, true) }; return SendInput(4, inputs, Marshal.SizeOf<INPUT>()) == 4; }
    internal static void SendEscape() { var inputs = new[] { Key(VK_ESCAPE), Key(VK_ESCAPE, true) }; SendInput(2, inputs, Marshal.SizeOf<INPUT>()); }

    internal static (nint Handle, Rectangle Bounds)? FindMenu(nint target, Point? click = null)
    {
        GetWindowThreadProcessId(target, out var targetPid);
        (nint, Rectangle)? result = null;
        long nearestDistance = long.MaxValue;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            // Modern Notepad uses a XAML popup, not a classic #32768 menu.
            var className = ClassName(window);
            if (className is not ("#32768" or "Xaml_WindowedPopupClass" or "Microsoft.UI.Content.PopupWindowSiteBridge")) return true;
            GetWindowThreadProcessId(window, out var pid);
            if (pid != targetPid || !GetWindowRect(window, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return true;
            if (click is not { } point) { result = (window, rect.Bounds); return false; }
            var dx = (long)point.X - Math.Clamp(point.X, rect.Left, rect.Right);
            var dy = (long)point.Y - Math.Clamp(point.Y, rect.Top, rect.Bottom);
            var distance = dx * dx + dy * dy;
            if (distance < nearestDistance) { nearestDistance = distance; result = (window, rect.Bounds); }
            return true;
        }, 0);
        return result;
    }
}
