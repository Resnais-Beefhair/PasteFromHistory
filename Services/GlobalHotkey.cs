using PasteFromHistory.Core;

namespace PasteFromHistory.Services;

internal sealed class GlobalHotkey : IDisposable
{
    private readonly Func<int, HotkeyGesture, bool> register;
    private readonly Action<int> unregister;
    internal HotkeyGesture? Current { get; private set; }
    internal int Id { get; private set; }

    internal GlobalHotkey(nint window, Func<int, HotkeyGesture, bool>? register = null, Action<int>? unregister = null)
    {
        this.register = register ?? ((id, gesture) => Native.RegisterHotKey(window, id, gesture.Modifiers | 0x4000, gesture.Key));
        this.unregister = unregister ?? (id => Native.UnregisterHotKey(window, id));
    }

    internal bool Set(bool enabled, HotkeyGesture gesture)
    {
        if (!enabled) { Dispose(); return true; }
        if (!gesture.IsValid) return false;
        if (Current == gesture) return true;
        var nextId = Id == 1 ? 2 : 1;
        // Reserve the new combination before releasing the working one. A failed
        // registration must not silently remove the user's previous shortcut.
        if (!register(nextId, gesture)) return false;
        if (Id != 0) unregister(Id);
        Id = nextId; Current = gesture;
        return true;
    }

    public void Dispose()
    {
        if (Id != 0) unregister(Id);
        Id = 0; Current = null;
    }
}
