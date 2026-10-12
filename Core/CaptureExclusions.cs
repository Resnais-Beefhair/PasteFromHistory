namespace PasteFromHistory.Core;

public static class CaptureExclusions
{
    public const int MaxProcesses = 100;

    public static bool TryParse(string text, out string[] names, out string? error) => TryNormalize(
        text.Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), out names, out error);

    public static bool TryNormalize(IEnumerable<string?>? input, out string[] names, out string? error)
    {
        names = []; error = null;
        if (input == null) { error = "The excluded application list is invalid. Enter process names, one per line."; return false; }
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var value in input)
        {
            if (++count > MaxProcesses) { error = $"Use at most {MaxProcesses} excluded application names."; return false; }
            var name = NormalizeName(value);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".."
                || name.Any(c => char.IsControl(c) || "\\/:*?\"<>|;,".Contains(c)))
            { error = "Use executable names such as KeePass.exe, one per line. Paths and wildcards are not supported."; return false; }
            result.Add(name);
        }
        names = result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    public static bool ShouldSkip(IEnumerable<string?>? excluded, string? owner, string? foreground)
    {
        // A malformed saved rule must not silently turn a privacy control off.
        if (!TryNormalize(excluded, out var names, out _)) return true;
        if (names.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(foreground)) return true;
        return names.Contains(NormalizeName(owner), StringComparer.OrdinalIgnoreCase)
            || names.Contains(NormalizeName(foreground), StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim() ?? "";
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
