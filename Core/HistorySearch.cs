namespace PasteFromHistory.Core;

/// <summary>Deterministic multi-word search with a bounded, conservative typo fallback.</summary>
public static class HistorySearch
{
    public const int MaxQueryLength = 512;
    public const int MaxTerms = 16;
    public const int MaxFuzzyCharacters = 64 * 1024;
    public const int MaxFuzzyWords = 4096;

    public static IEnumerable<ClipEntry> Find(IEnumerable<ClipEntry> entries, string query)
    {
        var parsed = Parse(query);
        var approximate = new List<ClipEntry>();
        foreach (var entry in entries)
        {
            var rank = Rank(entry, parsed);
            if (rank == 0) yield return entry;
            else if (rank == 1) approximate.Add(entry);
        }
        // Recent-first input order is stable inside each tier. Pins do not reorder it.
        foreach (var entry in approximate) yield return entry;
    }

    public static bool Matches(ClipEntry entry, string query) => Rank(entry, Parse(query)) >= 0;

    private sealed record Query(string[] Terms, bool Literal);

    private static Query Parse(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return new([], false);
        // Do not silently discard query suffixes. Oversized queries become one exact
        // phrase and never trigger an unbounded number of fuzzy comparisons.
        if (query.Length > MaxQueryLength) return new([query], true);
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return terms.Length > MaxTerms ? new([query], true) : new(terms, false);
    }

    private static int Rank(ClipEntry entry, Query query)
    {
        if (query.Terms.Length == 0) return 0;
        var fields = Fields(entry).ToArray();
        var missing = query.Terms.Where(term => !fields.Any(field => field.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (missing.Length == 0) return 0;
        // Numbers, short words, punctuation and identifiers need exact matching.
        // This prevents near-looking invoice numbers or paths from becoming matches.
        if (query.Literal || missing.Any(term => term.Length is < 4 or > 32 || !term.All(char.IsLetter))) return -1;
        // AND search can stop as soon as one term is absent. A broad miss query
        // must not compare every word to all sixteen missing query terms.
        return missing.All(term => FuzzyMatch(fields, term)) ? 1 : -1;
    }

    private static bool FuzzyMatch(string[] fields, string term)
    {
        var characters = MaxFuzzyCharacters;
        var words = MaxFuzzyWords;
        foreach (var field in fields)
        {
            var span = field.AsSpan(0, Math.Min(field.Length, characters));
            characters -= span.Length;
            for (var offset = 0; offset < span.Length;)
            {
                if (!char.IsLetterOrDigit(span[offset])) { offset++; continue; }
                var start = offset++;
                while (offset < span.Length && char.IsLetterOrDigit(span[offset])) offset++;
                // A truncated final word must not become a false short-word match.
                if (offset == span.Length && span.Length < field.Length && char.IsLetterOrDigit(field[offset])) break;
                var word = span[start..offset];
                if (--words < 0) return false;
                if (word.Length is < 3 or > 33 || !LettersOnly(word)) continue;
                if (WithinOneEdit(word, term)) return true;
            }
            if (characters == 0) break;
        }
        return false;
    }

    private static IEnumerable<string> Fields(ClipEntry entry)
    {
        // User labels and optional source metadata remain searchable even for a
        // large clip whose text exhausts the typo-search budget.
        yield return entry.Name;
        yield return entry.Collection;
        yield return entry.SourceApp;
        yield return entry.Title;
        foreach (var path in entry.Paths) yield return path;
        yield return entry.Text;
    }

    private static bool Same(char first, char second) => char.ToUpperInvariant(first) == char.ToUpperInvariant(second);

    private static bool LettersOnly(ReadOnlySpan<char> text)
    {
        foreach (var character in text) if (!char.IsLetter(character)) return false;
        return true;
    }

    private static bool WithinOneEdit(ReadOnlySpan<char> candidate, ReadOnlySpan<char> query)
    {
        if (Math.Abs(candidate.Length - query.Length) > 1) return false;
        var index = 0;
        while (index < candidate.Length && index < query.Length && Same(candidate[index], query[index])) index++;
        if (index == candidate.Length || index == query.Length) return true;
        if (candidate.Length == query.Length)
        {
            // One substitution, or one adjacent transposition ("adn" -> "and").
            if (Equal(candidate[(index + 1)..], query[(index + 1)..])) return true;
            return index + 1 < candidate.Length && Same(candidate[index], query[index + 1])
                && Same(candidate[index + 1], query[index]) && Equal(candidate[(index + 2)..], query[(index + 2)..]);
        }
        return candidate.Length > query.Length
            ? Equal(candidate[(index + 1)..], query[index..])
            : Equal(candidate[index..], query[(index + 1)..]);
    }

    private static bool Equal(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        => first.Equals(second, StringComparison.OrdinalIgnoreCase);
}
