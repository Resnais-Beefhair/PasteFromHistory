using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PasteFromHistory.Services;

/// <summary>Checks accessibility metadata, never the contents of a text field.</summary>
internal sealed class EditableTargetProbe
{
    private readonly Func<Point, nint, bool> inspect;
    private readonly TimeSpan timeout;
    private readonly Action<string>? trace;
    private int busy;

    internal EditableTargetProbe(Func<Point, nint, bool>? inspect = null, TimeSpan? timeout = null, Action<string>? trace = null)
    {
        this.trace = trace;
        this.inspect = inspect ?? Inspect;
        this.timeout = timeout ?? TimeSpan.FromMilliseconds(600);
    }

    internal async Task<bool> CheckAsync(Point point, nint window)
    {
        // One outstanding provider call at most, even if a broken provider hangs.
        // Do not queue more work or block the mouse hook / application UI thread.
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { Log("reject=probe-busy"); return false; }
        var started = Stopwatch.GetTimestamp();
        var work = Task.Run(() =>
        {
            try
            {
                // Thread-pool threads are MTA; no AutomationElement crosses threads.
                var editable = inspect(point, window);
                var elapsed = Stopwatch.GetElapsedTime(started);
                Log($"complete editable={editable} elapsed-ms={elapsed.TotalMilliseconds:F1} within-deadline={elapsed < timeout}");
                return editable && elapsed < timeout;
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ElementNotAvailableException
                or NotSupportedException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException)
            { Log("reject=provider-exception type=" + ex.GetType().Name); return false; }
            finally { Volatile.Write(ref busy, 0); }
        });
        try { return await work.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { Log($"reject=timeout elapsed-ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}"); return false; }
    }

    private bool Inspect(Point point, nint window)
    {
        if (window == 0 || !Native.IsWindow(window)) return Reject("missing-window");
        if (Native.Root(Native.WindowFromPoint(new Native.POINT(point))) != Native.Root(window))
            return Reject("point-window-changed");
        Native.GetWindowThreadProcessId(window, out var process);
        var position = new System.Windows.Point(point.X, point.Y);
        var element = AutomationElement.FromPoint(position);
        for (var depth = 0; element != null && depth < 8; depth++)
        {
            var current = element.Current;
            if (trace != null)
            {
                // Deliberately exclude Name, Value, text, window titles and paths.
                // A diagnostic provider failure must not change the eligibility decision.
                try
                {
                    Log($"element depth={depth} type={current.ControlType.ProgrammaticName} class={SafeMetadata(current.ClassName)}"
                        + $" framework={SafeMetadata(current.FrameworkId)} focusable={current.IsKeyboardFocusable} enabled={current.IsEnabled}"
                        + $" offscreen={current.IsOffscreen} password={current.IsPassword} bounds-match={current.BoundingRectangle.Contains(position)}"
                        + $" process-match={current.ProcessId == process}");
                }
                catch (Exception ex) { Log("metadata-unavailable type=" + ex.GetType().Name); }
            }
            if (current.ProcessId != process) return Reject("different-process");
            if (!current.IsEnabled) return Reject("disabled");
            if (current.IsOffscreen) return Reject("offscreen");
            if (current.IsPassword) return Reject("password");
            if (!current.BoundingRectangle.Contains(position)) return Reject("point-outside-bounds");
            var type = current.ControlType;
            if (type == ControlType.Edit || type == ControlType.Document)
            {
                // Word exposes a writable Edit child whose keyboard focus is
                // delegated to its containing Document. Explicit writable metadata
                // is the requirement; focusability is not an editability signal.
                // Ask for supported values explicitly. Missing metadata is not proof
                // of editability, and a read-only pattern always vetoes the result.
                var valueReadOnly = element.GetCurrentPropertyValue(ValuePattern.IsReadOnlyProperty, true);
                object textReadOnly = AutomationElement.NotSupported;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                    textReadOnly = ((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                Log($"readonly value={ReadOnlyLabel(valueReadOnly)} text={ReadOnlyLabel(textReadOnly)}");
                if (!IsWritable(valueReadOnly, textReadOnly)) return Reject("readonly-or-unknown");
                return true;
            }
            // Text runs may live inside an editor. Do not climb from a button,
            // scrollbar, image, menu, or generic window into a containing document.
            if (type != ControlType.Text && type != ControlType.Group && type != ControlType.Custom) return Reject("non-editor-control-type");
            element = TreeWalker.RawViewWalker.GetParent(element);
        }
        return Reject("no-editor-within-ancestor-limit");
    }

    private static string SafeMetadata(string value) => string.Concat(value.Take(128).Select(character => char.IsControl(character) ? '?' : character));
    private static string ReadOnlyLabel(object value) => value is bool flag ? flag.ToString() :
        ReferenceEquals(value, AutomationElement.NotSupported) ? "NotSupported" : "MixedOrUnknown";
    private bool Reject(string reason) { Log("reject=" + reason); return false; }
    private void Log(string message)
    {
        if (trace == null) return;
        try { trace("editor-probe " + message); } catch (Exception) { /* Test diagnostics must not affect input. */ }
    }

    internal static bool IsWritable(object valueReadOnly, object textReadOnly) =>
        valueReadOnly is not true && textReadOnly is not true
        && (valueReadOnly is false || textReadOnly is false);
}
