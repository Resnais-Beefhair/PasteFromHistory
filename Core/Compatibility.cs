namespace PasteFromHistory.Core;

internal static class Compatibility
{
    // Keep storage, encryption, IPC, and registry identities compatible with the
    // original local builds. Renaming these would strand existing encrypted clips
    // or allow two versions to observe the clipboard at the same time.
    internal const string LegacyName = "Leikehistoria";
}
