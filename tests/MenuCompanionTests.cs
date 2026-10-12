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
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] private static extern bool EndMenu();
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
