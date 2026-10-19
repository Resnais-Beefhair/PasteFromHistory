using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using PasteFromHistory.Services;
using PasteFromHistory.UI;

namespace PasteFromHistory.Tests;

internal static class EditorTargetTests
{
    internal static void Run(Action<bool, string> check)
    {
        var unknown = new object();
        check(!EditableTargetProbe.IsWritable(unknown, unknown), "Missing editability metadata is rejected");
        check(EditableTargetProbe.IsWritable(false, unknown), "Writable value metadata is recognized");
        check(EditableTargetProbe.IsWritable(unknown, false), "Writable document metadata is recognized");
        check(!EditableTargetProbe.IsWritable(true, false) && !EditableTargetProbe.IsWritable(false, true),
            "Read-only metadata vetoes conflicting writable metadata");

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var probe = new EditableTargetProbe((_, _) =>
        {
            Interlocked.Increment(ref calls); entered.Set(); release.Wait(); return true;
        }, TimeSpan.FromMilliseconds(120));
        try
        {
            var pending = probe.CheckAsync(default, 1);
            check(entered.Wait(3000), "Provider probe starts off the caller thread");
            check(!probe.CheckAsync(default, 1).GetAwaiter().GetResult() && calls == 1,
                "Slow provider does not create parallel calls or a queued backlog");
            check(!pending.GetAwaiter().GetResult(), "Provider timeout fails closed without waiting for the provider");
            check(!probe.CheckAsync(default, 1).GetAwaiter().GetResult() && calls == 1,
                "Timed-out provider remains bounded to one worker until it returns");
        }
        finally { release.Set(); }
        foreach (var failure in new Exception[] { new COMException("Unavailable fixture"), new ElementNotAvailableException("Closed fixture") })
        {
            var failing = new EditableTargetProbe((_, _) => throw failure);
            check(!failing.CheckAsync(default, 1).GetAwaiter().GetResult(), "Unavailable provider fails closed: " + failure.GetType().Name);
        }

        foreach (var scale in new[] { 1, 1.5, 2 })
        {
            var size = new Size((int)(240 * scale), (int)(46 * scale));
            var gap = (int)(6 * scale);
            foreach (var work in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -200, 1920, 1040) })
            {
                var click = new Point(work.Left + 800, work.Top + 400);
                var location = CompanionPlacement.Locate(click, size, work, null, gap);
                check(location.X == click.X + gap && location.Y + size.Height == click.Y - gap,
                    $"Unknown menu stays one small gap from click at {scale * 100}% / origin {work.Left}");
                foreach (var corner in new[] { work.Location, new Point(work.Right - 1, work.Top),
                    new Point(work.Left, work.Bottom - 1), new Point(work.Right - 1, work.Bottom - 1) })
                    check(work.Contains(new Rectangle(CompanionPlacement.Locate(corner, size, work, null, gap), size)),
                        $"Companion stays within work area at {scale * 100}% / {corner}");
                foreach (var menu in new[] { new Rectangle(click.X, click.Y, 300, 400),
                    new Rectangle(click.X - 300, click.Y - 350, 300, 350) })
                {
                    var placed = new Rectangle(CompanionPlacement.Locate(click, size, work, menu, gap), size);
                    check(work.Contains(placed) && !placed.IntersectsWith(menu),
                        $"Companion avoids native menu at {scale * 100}% / origin {work.Left}");
                }
            }
        }
    }

    internal static void RunInteractive(Action<bool, string> check)
    {
        using var form = new Form { Text = "PasteFromHistory – editable target tests", StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80), ClientSize = new Size(600, 440) };
        using var edit = new TextBox { Parent = form, Bounds = new Rectangle(20, 20, 240, 30) };
        using var readOnly = new TextBox { Parent = form, Bounds = new Rectangle(20, 70, 240, 30), ReadOnly = true };
        using var disabled = new TextBox { Parent = form, Bounds = new Rectangle(20, 120, 240, 30), Enabled = false };
        using var rich = new RichTextBox { Parent = form, Bounds = new Rectangle(20, 170, 240, 80) };
        using var button = new Button { Parent = form, Text = "Non-editor", Bounds = new Rectangle(300, 20, 240, 35) };
        using var label = new Label { Parent = form, Text = "Static text", Bounds = new Rectangle(300, 80, 240, 35) };
        using var surface = new Panel { Parent = form, BackColor = Color.DarkBlue, Bounds = new Rectangle(300, 170, 240, 180) };
        form.Show(); form.Activate(); edit.Focus(); Application.DoEvents();
        var probe = new EditableTargetProbe(timeout: TimeSpan.FromSeconds(3));
        bool Inspect(Control control)
        {
            var point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
            var result = probe.CheckAsync(point, form.Handle);
            WaitUntil(() => result.IsCompleted);
            return result.GetAwaiter().GetResult();
        }
        check(Inspect(edit), "UI Automation recognizes a writable native text field");
        check(!Inspect(readOnly), "UI Automation rejects a read-only text field");
        check(!Inspect(disabled), "UI Automation rejects a disabled text field");
        check(Inspect(rich), "UI Automation recognizes a writable rich-text document");
        rich.ReadOnly = true;
        check(!Inspect(rich), "UI Automation rejects a read-only rich-text document");
        check(!Inspect(button), "A button does not inherit editability from the focused text field");
        check(!Inspect(label), "Static text does not inherit editability from the focused text field");
        check(!Inspect(surface), "A game-like drawing surface does not offer the companion");
        check(edit.Focused, "Eligibility checks leave keyboard focus unchanged");

        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var mouse = new MouseObserver(dispatcher);
        PasteTarget? down = null;
        mouse.RightButtonDown += (_, target) => down = target;
        mouse.SetEnabled(true);
        var original = Cursor.Position;
        try
        {
            var point = rich.PointToScreen(new Point(20, 20));
            Cursor.Position = point;
            var input = new Native.INPUT { Type = 0, Data = new() { Mouse = new() { Flags = 0x0008 } } }; // right down
            Native.SendInput(1, [input], Marshal.SizeOf<Native.INPUT>());
            WaitUntil(() => down != null);
            check(down!.Focus == rich.Handle, "Right-click saves the clicked native field instead of a different focused field");
        }
        finally
        {
            var up = new Native.INPUT { Type = 0, Data = new() { Mouse = new() { Flags = 0x0010 } } };
            Native.SendInput(1, [up], Marshal.SizeOf<Native.INPUT>());
            Application.DoEvents(); Native.SendEscape(); Cursor.Position = original;
        }
    }

    private static void WaitUntil(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready() && timer.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
        if (!ready()) throw new TimeoutException("Interactive editor fixture did not respond.");
    }
}
