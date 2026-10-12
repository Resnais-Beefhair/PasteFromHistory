namespace PasteFromHistory.Core;

public readonly record struct HotkeyGesture(uint Modifiers, uint Key)
{
    public static HotkeyGesture Default => new(6, 0x56); // Ctrl + Shift + V
    public static IEnumerable<uint> SupportedKeys => Enumerable.Range(0x30, 10)
        .Concat(Enumerable.Range(0x41, 26)).Concat(Enumerable.Range(0x70, 24))
        .Where(key => key != 0x7B).Select(key => (uint)key); // F12 is reserved for the debugger.

    public bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 && SupportedKeys.Contains(Key);

    public static string KeyName(uint key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + (key - 0x70 + 1),
        _ => "Unknown"
    };

    public override string ToString() => string.Join("+", new[]
    {
        (Modifiers & 2) != 0 ? "Ctrl" : null,
        (Modifiers & 1) != 0 ? "Alt" : null,
        (Modifiers & 4) != 0 ? "Shift" : null,
        KeyName(Key)
    }.Where(part => part != null));
}
