using System.Net;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PasteFromHistory.Core;

public enum TextTransformKind { Edit, CleanWhitespace, JoinWrappedLines, Uppercase, Lowercase, Markdown, CleanUrl }

/// <summary>Local, deterministic conversions. Callers keep the original clip and preview the result.</summary>
public static class TextTransforms
{
    public const int MaxInputCharacters = 512 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id", "utm_source_platform",
        "utm_creative_format", "utm_marketing_tactic", "gclid", "dclid", "fbclid", "msclkid", "mc_cid", "mc_eid",
        "_hsenc", "_hsmi", "igshid"
    };
    private static readonly HashSet<string> SignedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "sig", "signature", "token", "access_token", "auth", "authorization", "oauth_signature", "expires",
        "policy", "key-pair-id", "awsaccesskeyid", "googleaccessid"
    };

    public static string Label(TextTransformKind kind) => kind switch
    {
        TextTransformKind.Edit => "Edit text",
        TextTransformKind.CleanWhitespace => "Clean whitespace",
        TextTransformKind.JoinWrappedLines => "Join wrapped lines",
        TextTransformKind.Uppercase => "UPPERCASE",
        TextTransformKind.Lowercase => "lowercase",
        TextTransformKind.Markdown => "HTML to Markdown",
        TextTransformKind.CleanUrl => "Remove URL tracking",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string Description(TextTransformKind kind) => kind switch
    {
        TextTransformKind.Edit => "Edit the result before pasting. Your saved clip stays unchanged.",
        TextTransformKind.CleanWhitespace => "Trim lines and collapse horizontal whitespace. Keep line breaks and paragraphs.",
        TextTransformKind.JoinWrappedLines => "Join wrapped text with spaces. Keep blank paragraphs and separate list items; keep hyphens.",
        TextTransformKind.Uppercase => "Convert letters to uppercase using culture-independent rules.",
        TextTransformKind.Lowercase => "Convert letters to lowercase using culture-independent rules.",
        TextTransformKind.Markdown => "Convert available HTML headings, emphasis, links, lists and paragraphs. Complex formatting becomes text. No HTML: keep plain text.",
        TextTransformKind.CleanUrl => "Clean one HTTP(S) URL. Remove known tracking keys only; keep other parameters and the fragment. Recognized signed/token URLs stay unchanged.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string Apply(string text, string? html, TextTransformKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxInputCharacters || kind == TextTransformKind.Markdown && html?.Length > MaxInputCharacters)
            throw new ArgumentException("This clip is too long to transform (maximum 512K characters). Use normal paste instead.");
        var result = kind switch
        {
            TextTransformKind.Edit => text,
            TextTransformKind.CleanWhitespace => CleanWhitespace(text),
            TextTransformKind.JoinWrappedLines => JoinWrappedLines(text),
            TextTransformKind.Uppercase => text.ToUpperInvariant(),
            TextTransformKind.Lowercase => text.ToLowerInvariant(),
            TextTransformKind.Markdown => string.IsNullOrWhiteSpace(html) ? text : ToMarkdown(html),
            TextTransformKind.CleanUrl => CleanUrl(text),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (result.Length > MaxInputCharacters) throw OutputTooLong();
        return result;
    }

    private static string Replace(string text, string pattern, string replacement) => Regex.Replace(text, pattern,
        replacement, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);

    private static string NormalizeLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string CleanWhitespace(string text) => string.Join(Environment.NewLine,
        NormalizeLines(text).Split('\n').Select(line => Replace(line, @"[\t\f\v\p{Zs}]+", " ").Trim())).Trim();

    private static string JoinWrappedLines(string text)
    {
        var result = new StringBuilder();
        var paragraph = false;
        foreach (var line in NormalizeLines(CleanWhitespace(text)).Split('\n'))
        {
            if (line.Length == 0) { paragraph = result.Length > 0; continue; }
            if (result.Length > 0)
            {
                var listItem = Regex.IsMatch(line, @"^([-*•]|[0-9]+[.)])\s", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout);
                result.Append(paragraph ? Environment.NewLine + Environment.NewLine : listItem ? Environment.NewLine : " ");
            }
            result.Append(line); paragraph = false;
        }
        return result.ToString();
    }

    private static string CleanUrl(string text)
    {
        var value = text.Trim();
        if (value.Length > 8192 || value.Any(char.IsWhiteSpace) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")) return text;
        var fragment = value.IndexOf('#');
        var query = value.IndexOf('?');
        if (query < 0 || fragment >= 0 && query > fragment) return text;
        var end = fragment < 0 ? value.Length : fragment;
        var parts = value[(query + 1)..end].Split('&');
        string Key(string part) => WebUtility.UrlDecode(part.Split('=', 2)[0]);
        // Some providers sign the exact query bytes. Preserve all such URLs, including
        // the tracking-looking fields, rather than silently invalidating a signature.
        if (parts.Select(Key).Any(key => SignedParameters.Contains(key)
            || key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("x-goog-", StringComparison.OrdinalIgnoreCase))) return text;
        var kept = parts.Where(part => !TrackingParameters.Contains(Key(part))).ToArray();
        if (kept.Length == parts.Length) return text;
        var cleaned = value[..query] + (kept.Length == 0 ? "" : "?" + string.Join('&', kept)) + value[end..];
        var start = text.IndexOf(value, StringComparison.Ordinal);
        return text[..start] + cleaned + text[(start + value.Length)..];
    }

    private static string ToMarkdown(string html)
    {
        const string fragmentStart = "<!--StartFragment-->";
        const string fragmentEnd = "<!--EndFragment-->";
        if (html.TrimStart('\uFEFF').StartsWith("Version:", StringComparison.OrdinalIgnoreCase)) html = CfHtmlSelection(html);
        else
        {
            var start = html.IndexOf(fragmentStart, StringComparison.OrdinalIgnoreCase);
            var end = html.IndexOf(fragmentEnd, StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end >= start + fragmentStart.Length) html = html[(start + fragmentStart.Length)..end];
        }
        var result = new StringBuilder();
        var anchors = new Stack<string?>();
        var lists = new List<int>(); // -1 = unordered; otherwise next ordinal.
        string? skip = null;
        var preformatted = false;

        void Write(string value)
        {
            if (value.Length > MaxInputCharacters - result.Length) throw OutputTooLong();
            result.Append(value);
        }
        void NewLines(int count)
        {
            while (result.Length > 0 && result[^1] is ' ' or '\t') result.Length--;
            if (result.Length == 0) return;
            var present = 0;
            for (var i = result.Length - 1; i >= 0 && result[i] == '\n'; i--) present++;
            if (present < count) Write(new string('\n', count - present));
        }
        void Text(string value)
        {
            if (skip != null) return;
            value = WebUtility.HtmlDecode(value);
            value = preformatted ? NormalizeLines(value) : Replace(value, @"\s+", " ");
            if (result.Length == 0 || result[^1] == '\n') value = value.TrimStart(' ');
            Write(EscapeMarkdown(value));
        }

        foreach (var token in Tokens(html))
        {
            if (!token.IsTag) { Text(token.Text); continue; }
            var tag = token.Text.AsSpan(1, token.Text.Length - 2).Trim();
            if (tag.Length == 0 || tag[0] is '!' or '?') continue;
            var closing = tag[0] == '/';
            if (closing) tag = tag[1..].TrimStart();
            var length = 0;
            while (length < tag.Length && char.IsAsciiLetterOrDigit(tag[length])) length++;
            var name = tag[..length].ToString().ToLowerInvariant();
            if (skip != null) { if (closing && name == skip) skip = null; continue; }
            if (name is "script" or "style" or "head" or "iframe" or "object" or "svg") { if (!closing) skip = name; continue; }
            if (name is "p" or "div" or "section" or "article" or "blockquote") { NewLines(2); continue; }
            if (name is "br" or "tr") { NewLines(1); continue; }
            if (name is "td" or "th") { if (closing) Write("\t"); continue; }
            if (name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6')
            {
                NewLines(2); if (!closing) Write(new string('#', name[1] - '0') + " "); continue;
            }
            if (name is "strong" or "b") { Write("**"); continue; }
            if (name is "em" or "i") { Write("*"); continue; }
            if (name is "ul" or "ol")
            {
                NewLines(1);
                if (closing) { if (lists.Count > 0) lists.RemoveAt(lists.Count - 1); }
                else if (lists.Count < 32) lists.Add(name == "ol" ? 1 : -1);
                continue;
            }
            if (name == "li")
            {
                NewLines(1);
                if (!closing)
                {
                    Write(new string(' ', Math.Max(0, lists.Count - 1) * 2));
                    if (lists.Count > 0 && lists[^1] >= 0) { Write(lists[^1].ToString(CultureInfo.InvariantCulture) + ". "); lists[^1]++; }
                    else Write("- ");
                }
                continue;
            }
            if (name == "a")
            {
                if (!closing)
                {
                    var href = Attribute(tag[length..].ToString(), "href");
                    if (href != null) href = WebUtility.HtmlDecode(href);
                    var safe = href != null && !href.Any(char.IsControl) && Uri.TryCreate(href, UriKind.Absolute, out var link)
                        && link.Scheme is "https" or "http" or "mailto";
                    anchors.Push(safe ? href : null);
                    if (safe) Write("[");
                }
                else if (anchors.TryPop(out var href) && href != null)
                    Write("](" + href.Replace("\\", "%5C").Replace("(", "%28").Replace(")", "%29").Replace(" ", "%20") + ")");
                continue;
            }
            if (name == "img" && !closing)
            {
                var alt = Attribute(tag[length..].ToString(), "alt");
                if (alt != null) Text(alt);
                continue;
            }
            if (name == "pre") { NewLines(2); preformatted = !closing; }
        }
        var markdown = result.ToString().Trim();
        if (markdown.Length + (long)markdown.Count(character => character == '\n') * (Environment.NewLine.Length - 1) > MaxInputCharacters)
            throw OutputTooLong();
        return markdown.Replace("\n", Environment.NewLine);
    }

    private static ArgumentException OutputTooLong() => new("The transformed result is too long (maximum 512K characters). Use a smaller clip or normal paste.");

    private static string CfHtmlSelection(string html)
    {
        ArgumentException Invalid() => new("The copied HTML has invalid selection offsets. Choose Edit text or another action to use its plain-text version.");
        var fields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "StartHTML", "EndHTML", "StartFragment", "EndFragment", "StartSelection", "EndSelection" };
        var cursor = html.StartsWith('\uFEFF') ? 1 : 0;
        while (cursor < html.Length)
        {
            var end = html.IndexOfAny(['\r', '\n'], cursor);
            if (end < 0) break;
            var line = html[cursor..end];
            var colon = line.IndexOf(':');
            if (colon <= 0 || !line[..colon].All(char.IsAsciiLetter)) break;
            var name = line[..colon];
            if (names.Contains(name))
            {
                if (!int.TryParse(line[(colon + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    || value < -1 || !fields.TryAdd(name, value)) throw Invalid();
            }
            cursor = end + 1;
            if (html[end] == '\r' && cursor < html.Length && html[cursor] == '\n') cursor++;
        }
        var encoding = new UTF8Encoding(false, true);
        byte[] bytes;
        int headerEnd;
        try { bytes = encoding.GetBytes(html); headerEnd = encoding.GetByteCount(html.AsSpan(0, cursor)); }
        catch (EncoderFallbackException) { throw Invalid(); }
        bool Boundary(int offset) => offset == bytes.Length || (bytes[offset] & 0xC0) != 0x80;
        (int Start, int End)? Range(string startName, string endName)
        {
            var hasStart = fields.TryGetValue(startName, out var start);
            var hasEnd = fields.TryGetValue(endName, out var end);
            if (!hasStart && !hasEnd) return null;
            if (!hasStart || !hasEnd) throw Invalid();
            if (start == -1 && end == -1) return null;
            if (start < headerEnd || end < start || end > bytes.Length || !Boundary(start) || !Boundary(end)) throw Invalid();
            return (start, end);
        }
        static bool Contains((int Start, int End) outer, (int Start, int End) inner) => inner.Start >= outer.Start && inner.End <= outer.End;
        var document = Range("StartHTML", "EndHTML");
        var fragment = Range("StartFragment", "EndFragment");
        var selection = Range("StartSelection", "EndSelection");
        if (document is { } doc && ((fragment is { } frag && !Contains(doc, frag)) || (selection is { } sel && !Contains(doc, sel)))) throw Invalid();
        if (fragment is { } context && selection is { } selected && !Contains(context, selected)) throw Invalid();
        var chosen = selection ?? fragment ?? throw Invalid();
        try { return encoding.GetString(bytes, chosen.Start, chosen.End - chosen.Start); }
        catch (DecoderFallbackException) { throw Invalid(); }
    }

    private static string EscapeMarkdown(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var escape = character is '\\' or '*' or '_' or '[' or ']' or '<' or '>' or '`' or '#' or '!';
            if (result.Length > MaxInputCharacters - (escape ? 2 : 1)) throw OutputTooLong();
            if (escape) result.Append('\\');
            result.Append(character);
        }
        return result.ToString();
    }

    private static string? Attribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, @"(?:^|\s)" + name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);
        if (!match.Success) return null;
        return match.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value;
    }

    private readonly record struct HtmlToken(string Text, bool IsTag);

    // A bounded, forward-only scanner handles quoted > characters and comments.
    // This is a small formatting converter, not a browser or an HTML renderer.
    private static IEnumerable<HtmlToken> Tokens(string html)
    {
        var position = 0;
        while (position < html.Length)
        {
            if (html[position] != '<')
            {
                var end = html.IndexOf('<', position);
                if (end < 0) end = html.Length;
                yield return new(html[position..end], false); position = end; continue;
            }
            if (html.AsSpan(position).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = html.IndexOf("-->", position + 4, StringComparison.Ordinal);
                position = end < 0 ? html.Length : end + 3; continue;
            }
            var next = position + 1;
            if (next == html.Length || !(char.IsAsciiLetter(html[next]) || html[next] is '/' or '!' or '?'))
            { yield return new("<", false); position++; continue; }
            var quote = '\0';
            for (; next < html.Length; next++)
            {
                var character = html[next];
                if (quote != '\0') { if (character == quote) quote = '\0'; }
                else if (character is '\'' or '"') quote = character;
                else if (character == '>') break;
            }
            if (next == html.Length) { yield return new(html[position..], false); yield break; }
            yield return new(html[position..(next + 1)], true); position = next + 1;
        }
    }
}
