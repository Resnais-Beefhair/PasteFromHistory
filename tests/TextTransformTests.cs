using PasteFromHistory.Core;
using PasteFromHistory.UI;

namespace PasteFromHistory.Tests;

internal static class TextTransformTests
{
    internal static void Run(Action<bool, string> check)
    {
        var newline = Environment.NewLine;
        string Apply(string text, TextTransformKind kind, string? html = null) => TextTransforms.Apply(text, html, kind);
        check(Apply("  Invoice\t  42  \r\n\r\n  Paid\u00a0today  ", TextTransformKind.CleanWhitespace)
            == "Invoice 42" + newline + newline + "Paid today", "Whitespace cleanup preserves paragraphs and Unicode content");
        check(Apply("This sentence\nwas wrapped.\n\nA new\nparagraph.\n- First item\ncontinued\n- Second item", TextTransformKind.JoinWrappedLines)
            == "This sentence was wrapped." + newline + newline + "A new paragraph." + newline + "- First item continued" + newline + "- Second item",
            "Wrapped lines join within paragraphs while keeping list items separate");
        check(Apply("well-\nknown", TextTransformKind.JoinWrappedLines) == "well- known",
            "Joining lines does not guess whether a hyphen should be deleted");
        check(Apply("Ää € 🙂\r\nNext", TextTransformKind.Edit) == "Ää € 🙂\r\nNext", "Edit mode retains exact original Unicode and line endings");
        check(Apply("Ää abc", TextTransformKind.Uppercase) == "ÄÄ ABC" && Apply("ÄÄ ABC", TextTransformKind.Lowercase) == "ää abc",
            "Case transformations handle Unicode letters without altering symbols");

        const string url = "https://EXAMPLE.com/a%2Fb?item=A%2BB&utm_source=news&item=two+words&flag#Section";
        check(Apply(url, TextTransformKind.CleanUrl) == "https://EXAMPLE.com/a%2Fb?item=A%2BB&item=two+words&flag#Section",
            "URL cleanup preserves exact non-tracking bytes, duplicate keys and fragments");
        check(Apply(" https://example.com/?utm_source=x&FBCLID=123#part ", TextTransformKind.CleanUrl) == " https://example.com/#part ",
            "URL cleanup removes the query delimiter when all keys were tracking, preserving outer whitespace");
        check(Apply("https://example.com/?%75tm_source=x&ref=keep", TextTransformKind.CleanUrl) == "https://example.com/?ref=keep",
            "URL cleanup recognizes encoded known keys without rewriting remaining values");
        foreach (var value in new[] { "https://example.com/?utm_source=x&sig=secret", "https://example.com/?X-Amz-Signature=secret&utm_medium=x",
            "https://example.com/?token=secret&utm_campaign=x", "https://example.com/?X-Goog-Credential=secret&utm_term=x",
            "https://example.com/#part?utm_source=x", "mailto:test@example.com?utm_source=x", "Here is https://example.com/?utm_source=x" })
            check(Apply(value, TextTransformKind.CleanUrl) == value, "Signed, non-HTTP or non-single URLs stay unchanged: " + value);
        check(Apply("https://example.com/?utm_custom=keep&source=keep&ref=keep", TextTransformKind.CleanUrl)
            == "https://example.com/?utm_custom=keep&source=keep&ref=keep", "URL cleanup preserves unknown and functional parameter names");

        check(Apply("plain fallback", TextTransformKind.Markdown) == "plain fallback", "Markdown conversion keeps plain text when no HTML is available");
        var basic = Apply("fallback", TextTransformKind.Markdown, "<h2>Title</h2><p>Hello <strong>world</strong> &amp; <em>friends</em>.</p><p><a href='https://example.com/?a=1&amp;b=2'>Read more</a></p>");
        check(basic == "## Title" + newline + newline + "Hello **world** & *friends*." + newline + newline + "[Read more](https://example.com/?a=1&b=2)",
            "HTML headings, emphasis, entities and links convert to readable Markdown");
        check(Apply("fallback", TextTransformKind.Markdown, "<html><body>Outside<!--StartFragment--><p>Ä €</p><!--EndFragment-->Outside</body></html>") == "Ä €",
            "Raw HTML fragment markers exclude surrounding document content");
        var cfHtml = CfHtmlFixture(true);
        check(Apply("fallback", TextTransformKind.Markdown, cfHtml) == "**Selected €**",
            "CF_HTML honors UTF-8 selection offsets inside the broader fragment, excluding surrounding copied context");
        check(Apply("fallback", TextTransformKind.Markdown, CfHtmlFixture(false)) == "CONTEXT Ä € **Selected €** END CONTEXT",
            "CF_HTML uses UTF-8 fragment offsets when no selection is present, including spaced fragment markers");
        var euro = cfHtml.IndexOf("€</strong>", StringComparison.Ordinal);
        var splitCharacter = System.Text.Encoding.UTF8.GetByteCount(cfHtml[..euro]) + 1;
        var malformed = new[]
        {
            System.Text.RegularExpressions.Regex.Replace(cfHtml, "StartSelection:[0-9]+", "StartSelection:" + splitCharacter.ToString("D10")),
            System.Text.RegularExpressions.Regex.Replace(cfHtml, "EndSelection:[0-9]+", "EndSelection:9999999999"),
            System.Text.RegularExpressions.Regex.Replace(cfHtml, "StartFragment:[0-9]+", "StartFragment:0000000000"),
            "Version:1.0\r\nStartHTML:0000\r\n<html>Context<!--StartFragment-->Selected<!--EndFragment--></html>"
        };
        foreach (var invalid in malformed)
        {
            var blocked = false;
            try { Apply("plain fallback", TextTransformKind.Markdown, invalid); } catch (ArgumentException) { blocked = true; }
            check(blocked, "Malformed CF_HTML offsets fail explicitly rather than pasting unselected context");
        }
        var unsafeHtml = "<style>secret style</style><script>alert(1)</script><p><a href='javascript:alert(1)'>Label</a> <img src='https://example.com/image.png' alt='Image &amp; text'></p>";
        check(Apply("fallback", TextTransformKind.Markdown, unsafeHtml) == "Label Image & text",
            "Markdown conversion drops active content, rejects executable links and uses image alt text without fetching images");
        check(Apply("fallback", TextTransformKind.Markdown, "<ol><li>One</li><li>Two</li></ol>") == "1. One" + newline + "2. Two",
            "Ordered HTML lists preserve separate numbered items");
        check(Apply("fallback", TextTransformKind.Markdown, "<p title='a > b'>Literal [x] &lt;tag&gt; *</p>") == "Literal \\[x\\] \\<tag\\> \\*",
            "Quoted tag delimiters and literal Markdown syntax are handled without interpreting content as markup");
        var huge = new string('x', TextTransforms.MaxInputCharacters + 1);
        var rejected = false;
        try { Apply(huge, TextTransformKind.Uppercase); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Oversized transformations fail explicitly instead of truncating the source");
        var nested = string.Concat(Enumerable.Repeat("<ul><li>", 2000)) + "Item" + string.Concat(Enumerable.Repeat("</li></ul>", 2000));
        check(Apply("fallback", TextTransformKind.Markdown, nested).Length < 200_000,
            "Deeply nested HTML has bounded indentation and output growth");
        var expanded = string.Concat(Enumerable.Repeat("<ul>", 32)) + string.Concat(Enumerable.Repeat("<li>x</li>", 10_000));
        foreach (var input in new[] { expanded, new string('*', TextTransforms.MaxInputCharacters / 2 + 1) })
        {
            var blocked = false;
            try { Apply("fallback", TextTransformKind.Markdown, input); } catch (ArgumentException) { blocked = true; }
            check(blocked, "Markdown expansion is bounded during conversion before the result reaches the preview");
        }
    }

    internal static void RunUi(Action<bool, string> check, Action<Form, string> render)
    {
        var source = new ClipEntry { Text = "A formatted sample\r\nSecond line", Html = "<b>A formatted sample</b>", Rtf = "{\\rtf1 sample}", Pinned = true };
        using var dialog = new TransformPreviewForm(source);
        render(dialog, "transform-preview.png");
        var original = Controls(dialog).OfType<TextBox>().Single(box => box.AccessibleName == "Original clip text");
        var result = Controls(dialog).OfType<TextBox>().Single(box => box.AccessibleName == "Transformed text preview");
        var paste = Controls(dialog).OfType<Button>().Single(button => button.Text == "Paste result");
        check(original.ReadOnly && !result.ReadOnly && result.Text == source.Text, "Transform preview keeps an original beside the editable result");
        var action = Controls(dialog).OfType<ComboBox>().Single();
        action.SelectedItem = action.Items.Cast<object>().Single(item => item.ToString() == TextTransforms.Label(TextTransformKind.Uppercase));
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!paste.Enabled && waited.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
        check(result.Text == source.Text.ToUpperInvariant() && !result.ReadOnly && paste.Enabled && original.Text == source.Text,
            "Selecting a transformation prepares its editable preview without changing the original");
        dialog.Size = dialog.MinimumSize;
        dialog.PerformLayout(); Application.DoEvents();
        render(dialog, "transform-preview-small.png");
        var clipped = Controls(dialog).OfType<Button>().Where(button => button.Visible
            && (!button.Parent!.ClientRectangle.Contains(button.Bounds)
                || !dialog.ClientRectangle.Contains(dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle)))))
            .Select(button => $"{button.Text}: {button.Bounds} in {button.Parent!.ClientRectangle}").ToArray();
        check(clipped.Length == 0,
            "Transform dialog actions fit its minimum size" + (clipped.Length == 0 ? "" : $" at {dialog.DeviceDpi} DPI: " + string.Join("; ", clipped)));
        check(result.ClientSize.Height >= result.Font.Height * 5 && original.ClientSize.Height >= original.Font.Height * 5
            && new[] { result, original }.All(box => dialog.ClientRectangle.Contains(dialog.RectangleToClient(box.RectangleToScreen(box.ClientRectangle)))),
            "Minimum transform dialog keeps at least five lines visible in each preview at the active DPI");
        result.Text = "Edited just for this paste";
        paste.PerformClick();
        check(dialog.Result is { Html: null, Rtf: null, Pinned: true } && dialog.Result.Id == source.Id
            && dialog.Result.Text == "Edited just for this paste" && source.Text.StartsWith("A formatted") && source.Html != null,
            "Accepting an edited preview retains identity and metadata while leaving the original rich clip unchanged");
        using var empty = new TransformPreviewForm(source);
        Controls(empty).OfType<TextBox>().Single(box => box.AccessibleName == "Transformed text preview").Text = "";
        check(!Controls(empty).OfType<Button>().Single(button => button.Text == "Paste result").Enabled && empty.Result == null,
            "An empty preview cannot be pasted and does not create a result");
    }

    private static IEnumerable<Control> Controls(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Controls(child)) yield return descendant; }
    }

    private static string CfHtmlFixture(bool includeSelection)
    {
        string Header(int htmlStart, int htmlEnd, int fragmentStart, int fragmentEnd, int selectionStart, int selectionEnd) =>
            $"Version:1.0\r\nStartHTML:{htmlStart:D10}\r\nEndHTML:{htmlEnd:D10}\r\nStartFragment:{fragmentStart:D10}\r\nEndFragment:{fragmentEnd:D10}\r\n"
            + (includeSelection ? $"StartSelection:{selectionStart:D10}\r\nEndSelection:{selectionEnd:D10}\r\n" : "");
        const string before = "<html><body>Ä € OUTSIDE<!--StartFragment -->";
        const string prefix = "<p>CONTEXT Ä € ";
        const string selection = "<strong>Selected €</strong>";
        const string fragment = prefix + selection + " END CONTEXT</p>";
        const string after = "<!--EndFragment --></body></html>";
        int Bytes(string value) => System.Text.Encoding.UTF8.GetByteCount(value);
        var htmlStart = Bytes(Header(0, 0, 0, 0, 0, 0));
        var fragmentStart = htmlStart + Bytes(before);
        var selectionStart = fragmentStart + Bytes(prefix);
        return Header(htmlStart, htmlStart + Bytes(before + fragment + after), fragmentStart, fragmentStart + Bytes(fragment),
            selectionStart, selectionStart + Bytes(selection)) + before + fragment + after;
    }
}
