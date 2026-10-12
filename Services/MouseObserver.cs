using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PasteFromHistory.Services;

internal sealed class MouseObserver : IDisposable
{
    private readonly Native.HookProc callback;
    private nint hook;
    private PasteTarget? rightDownTarget;
    private readonly Control dispatcher;
    public event Action<Point, PasteTarget>? RightClick;
    public event Action<Point>? ButtonDown;

    public MouseObserver(Control dispatcher) { this.dispatcher = dispatcher; callback = OnMouse; }

    public void SetEnabled(bool enabled)
    {
        rightDownTarget = null;
        if (hook != 0) { Native.UnhookWindowsHookEx(hook); hook = 0; }
        if (enabled)
        {
            hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Right-click monitoring could not start.");
        }
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !dispatcher.IsDisposed)
        {
            var message = (int)wParam;
            if (message is Native.WM_RBUTTONUP or Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or 0x0207)
            {
                var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                // Accessibility software and remote desktops may inject mouse input too.
                // Observing those events is safe: this application never injects mouse events.
                {
                    var point = new Point(info.Point.X, info.Point.Y);
                    var root = Native.Root(Native.WindowFromPoint(info.Point));
                    // Capture focus before the destination app gives it to its menu.
                    if (message == Native.WM_RBUTTONDOWN) rightDownTarget = PasteTarget.Capture(root);
                    var destination = rightDownTarget ?? new PasteTarget(root);
                    if (message == Native.WM_RBUTTONUP)
                    {
                        rightDownTarget = null;
                    }
                    try
                    {
                        dispatcher.BeginInvoke(() =>
                        {
                            if (message == Native.WM_RBUTTONUP) RightClick?.Invoke(point, destination);
                            else ButtonDown?.Invoke(point);
                        });
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        // Observe only. Native application menus and mouse events always continue normally.
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose() { SetEnabled(false); GC.KeepAlive(callback); }
}
