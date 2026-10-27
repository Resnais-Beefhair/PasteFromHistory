using System.Runtime.InteropServices;
using PasteFromHistory.Services;
using PasteFromHistory.UI;

namespace PasteFromHistory.Tests;

internal static class MenuCompanionTests
{
    // Exercise an actual Win32 menu loop, including its mouse capture and focus.
    // Testing only Form.Visible misses menus cancelled by a second window.
    internal static void Run(Action<bool, string> check)
    {
        using var target = new Form { Text = "PasteFromHistory – menu regression test", ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), Size = new Size(380, 200) };
        using var field = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        target.Controls.Add(field);
        target.Show(); target.Activate(); field.Focus();
        Application.DoEvents();
        check(Native.GetForegroundWindow() == target.Handle && GetFocus() == field.Handle, "Menu test owns foreground and text focus");

        using var companion = new MenuCompanion();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var menu = CreatePopupMenu();
            if (menu == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                AppendMenu(menu, 0, 1, "Test menu item");
                bool opened = false, stayedOpen = false, focusKept = false, foregroundKept = false;
                Exception? failure = null;
                using var show = new System.Windows.Forms.Timer { Interval = 100 };
                show.Tick += (_, _) =>
                {
                    show.Stop();
                    try
                    {
                        var nativeMenu = Native.FindMenu(target.Handle);
                        opened = nativeMenu != null;
                        companion.ShowBeside(target.PointToScreen(new Point(50, 60)), PasteTarget.Capture(target.Handle));
                        Application.DoEvents();
                        foregroundKept = Native.GetForegroundWindow() == target.Handle;
                        focusKept = GetFocus() == field.Handle;
                        stayedOpen = nativeMenu != null && Native.IsWindowVisible(nativeMenu.Value.Handle);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { companion.Hide(); EndMenu(); }
                };
                show.Start();
                var point = target.PointToScreen(new Point(50, 60));
                TrackPopupMenuEx(menu, 0x100, point.X, point.Y, target.Handle, 0);
                if (failure != null) throw failure;
                check(opened, $"Native context menu opened (show {attempt})");
                check(foregroundKept, $"Companion preserves foreground window (show {attempt})");
                check(focusKept, $"Companion preserves text focus (show {attempt})");
                check(stayedOpen, $"Companion leaves native context menu open (show {attempt})");
            }
            finally { DestroyMenu(menu); }
        }

        var saved = PasteTarget.Capture(target.Handle);
        using var other = new TextBox { Parent = target, Visible = true };
        other.Focus();
        check(Native.FocusedWindow(target.Handle) == other.Handle, "Simulated menu moves focus away from text field");
        check(Native.RestoreFocus(saved.Window, saved.Focus) && field.Focused, "Paste restores original text control focus");
        using var unrelated = new Form();
        check(!Native.RestoreFocus(target.Handle, unrelated.Handle), "Focus restoration rejects a different window");
        check(!Native.RestoreFocus(target.Handle, 0), "Focus restoration rejects a missing control");
        CheckOwnedMenus(check, target, companion);
    }

    private static void CheckOwnedMenus(Action<bool, string> check, Form target, MenuCompanion companion)
    {
        using var windows = new OwnedMenuWindows();
        var menuBounds = new Rectangle(500, 400, 220, 240);
        var toolbarBounds = new Rectangle(500, 330, 390, 50);
        var menu = windows.Create(target.Handle, menuBounds);
        windows.Create(target.Handle, toolbarBounds);
        windows.Create(target.Handle, new Rectangle(1100, 700, 200, 80));
        var discovered = Native.FindMenu(target.Handle, menuBounds.Location);
        check(discovered is { } found && found.Handle == menu && found.Bounds == Rectangle.Union(menuBounds, toolbarBounds),
            "Native Word-style menu discovery keeps the nearest handle and reserves its adjacent toolbar");
        Native.SetForegroundWindow(menu); Application.DoEvents();
        check(Native.GetForegroundWindow() == menu && Native.IsTargetForeground(target.Handle),
            "An active recognized owned menu still belongs to its original document target");
        companion.ShowBeside(menuBounds.Location, PasteTarget.Capture(target.Handle));
        PumpFor(200);
        check(companion.Visible && Native.GetForegroundWindow() == menu,
            "Companion lifetime accepts an active owned menu without taking foreground focus");
        ShowWindow(menu, 0); PumpFor(200);
        check(!companion.Visible, "Companion closes when its tracked owned menu disappears");

        using var dialog = new Form { Owner = target, Text = "Synthetic unrelated owned dialog", Size = new Size(260, 140) };
        dialog.Show(); dialog.Activate(); Application.DoEvents();
        check(Native.GetForegroundWindow() == dialog.Handle && !Native.IsTargetForeground(target.Handle),
            "An unrelated owned dialog is not accepted as the document's context menu");
        var otherMenu = windows.Create(dialog.Handle, new Rectangle(800, 400, 200, 200));
        Native.SetForegroundWindow(otherMenu); Application.DoEvents();
        check(Native.GetForegroundWindow() == otherMenu && !Native.IsTargetForeground(target.Handle),
            "A recognized menu owned by another dialog cannot stand in for the original document");
    }

    private static void PumpFor(int milliseconds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
    }

    private sealed class OwnedMenuWindows : IDisposable
    {
        private const string ClassName = "Net UI Tool Window";
        private readonly WindowProcedure procedure = DefWindowProc;
        private readonly List<nint> windows = [];
        private readonly nint module = Native.GetModuleHandle(null);

        public OwnedMenuWindows()
        {
            var windowClass = new WindowClass { Style = 0, Procedure = procedure, ClassExtra = 0, WindowExtra = 0,
                Instance = module, Icon = 0, Cursor = 0, Background = 0, MenuName = null, ClassName = ClassName };
            if (RegisterClass(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public nint Create(nint owner, Rectangle bounds)
        {
            var window = CreateWindowEx(0x80, ClassName, "", 0x80000000, bounds.X, bounds.Y, bounds.Width, bounds.Height, owner, 0, module, 0);
            if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            windows.Add(window); ShowWindow(window, 4); return window; // SW_SHOWNOACTIVATE
        }

        public void Dispose()
        {
            foreach (var window in windows) if (Native.IsWindow(window)) DestroyWindow(window);
            UnregisterClass(ClassName, module); GC.KeepAlive(procedure);
        }
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedure Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint owner, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
