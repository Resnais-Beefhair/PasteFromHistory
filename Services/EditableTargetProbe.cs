using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PasteFromHistory.Services;

/// <summary>Checks accessibility metadata, never the contents of a text field.</summary>
internal sealed class EditableTargetProbe
{
    private readonly Func<Point, nint, bool> inspect;
    private readonly TimeSpan timeout;
    private int busy;

    internal EditableTargetProbe(Func<Point, nint, bool>? inspect = null, TimeSpan? timeout = null)
    {
        this.inspect = inspect ?? Inspect;
        this.timeout = timeout ?? TimeSpan.FromMilliseconds(600);
    }

    internal async Task<bool> CheckAsync(Point point, nint window)
    {
        // One outstanding provider call at most, even if a broken provider hangs.
        // Do not queue more work or block the mouse hook / application UI thread.
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        var started = Stopwatch.GetTimestamp();
        var work = Task.Run(() =>
        {
            try
            {
                // Thread-pool threads are MTA; no AutomationElement crosses threads.
                return inspect(point, window) && Stopwatch.GetElapsedTime(started) < timeout;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ElementNotAvailableException
                or NotSupportedException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException)
            { return false; }
            finally { Volatile.Write(ref busy, 0); }
        });
        try { return await work.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
    }

    private static bool Inspect(Point point, nint window)
    {
        if (window == 0 || !Native.IsWindow(window)
            || Native.Root(Native.WindowFromPoint(new Native.POINT(point))) != Native.Root(window)) return false;
        Native.GetWindowThreadProcessId(window, out var process);
        var position = new System.Windows.Point(point.X, point.Y);
        var element = AutomationElement.FromPoint(position);
        for (var depth = 0; element != null && depth < 8; depth++)
        {
            var current = element.Current;
            if (current.ProcessId != process || !current.IsEnabled || current.IsOffscreen
                || !current.BoundingRectangle.Contains(position)) return false;
            var type = current.ControlType;
            if (type == ControlType.Edit || type == ControlType.Document)
            {
                if (!current.IsKeyboardFocusable) return false;
                // Ask for supported values explicitly. Missing metadata is not proof
                // of editability, and a read-only pattern always vetoes the result.
                var valueReadOnly = element.GetCurrentPropertyValue(ValuePattern.IsReadOnlyProperty, true);
                object textReadOnly = AutomationElement.NotSupported;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                    textReadOnly = ((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                return IsWritable(valueReadOnly, textReadOnly);
            }
            // Text runs may live inside an editor. Do not climb from a button,
            // scrollbar, image, menu, or generic window into a containing document.
            if (type != ControlType.Text && type != ControlType.Group && type != ControlType.Custom) return false;
            element = TreeWalker.RawViewWalker.GetParent(element);
        }
        return false;
    }

    internal static bool IsWritable(object valueReadOnly, object textReadOnly) =>
        valueReadOnly is not true && textReadOnly is not true
        && (valueReadOnly is false || textReadOnly is false);
}
