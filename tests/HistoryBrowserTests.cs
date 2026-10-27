using PasteFromHistory.Core;
using PasteFromHistory.UI;

namespace PasteFromHistory.Tests;

internal static class HistoryBrowserTests
{
    internal static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Now;
        var oldFavorite = new ClipEntry
        {
            Text = "The archived support response", Name = "Support reply", Collection = "Work",
            SourceApp = "notepad.exe", Pinned = true, CapturedAt = now.AddDays(-40)
        };
        var older = new ClipEntry { Text = "Six day reference", Collection = "Work", SourceApp = "excel.exe", CapturedAt = now.AddDays(-6) };
        var recent = new ClipEntry
        {
            Text = "Blue calendar meeting", Name = "Appointment", Collection = "Personal",
            SourceApp = "notepad.exe", CapturedAt = now.AddDays(-1)
        };
        var newest = new ClipEntry { Text = "Fresh message", CapturedAt = now };
        var files = new ClipEntry { Kind = ClipKind.Files, Paths = [@"C:\Examples\browser-test.txt"], CapturedAt = now.AddMinutes(-2) };
        var history = new History();
        foreach (var entry in new[] { oldFavorite, older, recent, files, newest }) history.Add(entry, 100);

        using (var form = new HistoryForm(history, new(), false))
        {
            Show(form);
            var list = Controls(form).OfType<ListBox>().Single();
            Button Button(string text) => Controls(form).OfType<Button>().Single(button => button.Text == text);
            ComboBox Combo(string name) => Controls(form).OfType<ComboBox>().Single(combo => combo.AccessibleName == name);
            var search = Controls(form).OfType<TextBox>().Single(box => box.AccessibleName == "Search history");
            check(list.Items.Cast<ClipEntry>().Select(entry => entry.Id).SequenceEqual(new[] { newest.Id, files.Id, recent.Id, older.Id, oldFavorite.Id }),
                "Recent keeps actual copy order without promoting favorites");
            Button("Favorites").PerformClick();
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == oldFavorite.Id, "Favorites is a separate pinned-only scope");
            Search(search, "fresh");
            check(list.Items.Count == 0, "Favorites search does not escape its scope");
            Button("Recent").PerformClick();
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == newest.Id, "Switching scope preserves the search query");
            Search(search, "meeting blue");
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == recent.Id, "History browser accepts words in any order");
            Search(search, "calender");
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == recent.Id, "History browser finds a conservative one-letter typo");
            Search(search, "");
            Select(Combo("Collection"), "Work");
            check(list.Items.Count == 2 && list.Items.Cast<ClipEntry>().All(entry => entry.Collection == "Work"), "Collection filters include named and unnamed clips");
            Select(Combo("Source application"), "excel.exe");
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == older.Id, "Source application and collection filters combine");
            Select(Combo("Copied date"), "Today");
            check(list.Items.Count == 0, "Today excludes older source-filtered clips");
            Select(Combo("Copied date"), "Last 7 days");
            check(list.Items.Count == 1, "Last seven days includes a six-day-old clip");
            Select(Combo("Source application"), "All applications");
            Select(Combo("Collection"), "All collections");
            Select(Combo("Copied date"), "Last 30 days");
            check(list.Items.Count == 4 && list.Items.Cast<ClipEntry>().All(entry => entry.Id != oldFavorite.Id), "Date filters apply equally to pinned content in Recent");
            Select(Combo("Copied date"), "Any time");
            Select(Combo("Content type"), "Files");
            check(list.Items.Count == 1 && !Button("Transform…").Enabled && !Button("Paste as text").Enabled,
                "File filter disables text-only transform and paste actions");
            Select(Combo("Content type"), "All types");
            list.SelectedItem = newest;
            form.BeginInvoke(new Action(() =>
            {
                var dialog = Application.OpenForms.OfType<ClipDetailsForm>().Single();
                Controls(dialog).OfType<TextBox>().Single(box => box.AccessibleName == "Clip name").Text = "A reusable reply";
                Controls(dialog).OfType<ComboBox>().Single(box => box.AccessibleName == "Clip collection").Text = "Support";
                Controls(dialog).OfType<Button>().Single(button => button.Text == "Save").PerformClick();
            }));
            Button("Rename…").PerformClick();
            var renamed = history.Entries.Single(entry => entry.Id == newest.Id);
            check(renamed.Name == "A reusable reply" && renamed.Collection == "Support" && renamed.Text == newest.Text,
                "Name dialog saves metadata without changing copied text");
            check(list.SelectedItem is ClipEntry { Name: "A reusable reply" } && Combo("Collection").Items.Cast<object>().Any(item => item.ToString() == "Support"),
                "Metadata editing refreshes the selected row and collection choices");
            Search(search, "reusable");
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == newest.Id, "Renamed clips are immediately searchable");
            search.Text = "archived";
            check(!list.Enabled && !Button("Paste").Enabled && !Button("Transform…").Enabled,
                "Pending search disables stale result clicks and actions");
            Search(search, "archived");
            check(list.Enabled && ((ClipEntry)list.Items[0]).Id == oldFavorite.Id,
                "Debounced search enables actions only after updating the result list");
            Search(search, "");
            form.Size = form.MinimumSize; form.PerformLayout(); Application.DoEvents();
            check(Controls(form).OfType<Button>().Where(button => button.Visible).All(button => button.Parent!.ClientRectangle.Contains(button.Bounds)),
                "History browser actions fit the small supported window");
            check(Controls(form).OfType<ComboBox>().Where(combo => combo.Visible).All(combo => combo.Parent!.ClientRectangle.Contains(combo.Bounds)),
                "History browser filters fit the small supported window");
            check(list.ClientSize.Height >= list.ItemHeight * 3, "Minimum browser size leaves room for at least three clip rows at the active DPI");
        }

        using (var form = new HistoryForm(history, new AppSettings { MenuCount = 2 }, true))
        {
            Show(form);
            var list = Controls(form).OfType<ListBox>().Single();
            var search = Controls(form).OfType<TextBox>().Single(box => box.AccessibleName == "Search history");
            Button Button(string text) => Controls(form).OfType<Button>().Single(button => button.Text == text);
            check(list.Items.Count == 2 && ((ClipEntry)list.Items[0]).Id == newest.Id && !list.Items.Cast<ClipEntry>().Any(entry => entry.Id == oldFavorite.Id),
                "Quick Recent limit is not consumed by an old favorite");
            Search(search, "archived");
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == oldFavorite.Id, "Quick search includes clips beyond the visible recent limit");
            Search(search, ""); Button("Favorites").PerformClick();
            check(list.Items.Count == 1 && ((ClipEntry)list.Items[0]).Id == oldFavorite.Id, "Quick Favorites exposes old pinned clips independently");
            var chosen = false; form.Chosen += (_, _, _) => chosen = true;
            form.BeginInvoke(new Action(() =>
            {
                var dialog = Application.OpenForms.OfType<TransformPreviewForm>().Single();
                dialog.DialogResult = DialogResult.Cancel; dialog.Close();
            }));
            Button("Transform…").PerformClick();
            check(!form.IsDisposed && form.Visible && !chosen && history.Entries.Single(entry => entry.Id == oldFavorite.Id).Text == oldFavorite.Text,
                "Cancelling a transform keeps the quick picker open and its stored clip unchanged");
            form.BeginInvoke(new Action(() =>
            {
                var dialog = Application.OpenForms.OfType<ClipDetailsForm>().Single();
                dialog.DialogResult = DialogResult.Cancel; dialog.Close();
            }));
            list.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(item => item.Text == "Rename…").PerformClick();
            check(!form.IsDisposed && form.Visible, "Quick picker remains open while cancelling a name dialog");
        }

        using var noSources = new HistoryForm(new History(), new(), false);
        check(!Controls(noSources).OfType<ComboBox>().Single(combo => combo.AccessibleName == "Source application").Enabled,
            "Source filter is disabled when no source metadata exists");
    }

    private static void Select(ComboBox combo, string value) =>
        combo.SelectedItem = combo.Items.Cast<object>().Single(item => item.ToString() == value);

    private static void Search(TextBox box, string query)
    {
        box.Text = query;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var list = Controls(box.FindForm()!).OfType<ListBox>().Single();
        while (!list.Enabled && clock.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(5); }
        if (!list.Enabled) throw new Exception("Debounced history search did not complete.");
    }

    private static void Show(Form form)
    {
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents(); form.PerformLayout();
    }

    private static IEnumerable<Control> Controls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Controls(child)) yield return nested;
        }
    }
}
