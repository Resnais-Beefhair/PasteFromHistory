using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasteFromHistory.Core;
using PasteFromHistory.Services;
using PasteFromHistory.UI;

namespace PasteFromHistory.Tests;

internal static class Program
{
    private static int checks;
    private static string output = "";
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }
    private static ClipEntry Text(string text) => new() { Kind = ClipKind.Text, Text = text };

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--ui")) { Application.Run(new TestHost()); return 0; }
        output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "artifacts");
        Directory.CreateDirectory(output);
        try
        {
            if (args.Contains("--menu-regression")) MenuCompanionTests.Run(Check);
            else Run();
            Console.WriteLine($"\n{checks} checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Run()
    {
        Check(new AppSettings().MenuCount == 10, "Ten recent entries by default");
        Check(!new AppSettings().HotkeyEnabled, "Global shortcut is opt-in by default");
        var normalized = new AppSettings { HistoryLimit = -1, MenuCount = 500 };
        normalized.Normalize();
        Check(normalized.HistoryLimit == 10 && normalized.MenuCount == 10, "Settings bounds");
        var history = new History();
        for (var i = 0; i < 15; i++) history.Add(Text("Clip " + i), 10);
        Check(history.Entries.Count == 10 && history.Entries[0].Text == "Clip 14", "Most recent first and count cap");
        history.Add(Text("Clip 8"), 10);
        Check(history.Entries.Count == 10 && history.Entries[0].Text == "Clip 8", "Duplicate promoted without extra row");
        history.Add(Text("Clip 8") with { Rtf = "{\\rtf1 styled}" }, 10);
        Check(history.Entries.Count(e => e.Text == "Clip 8") == 2, "Distinct formatting retained");
        Check(!history.Add(Text(new string('x', 4 * 1024 * 1024 + 1)), 10), "Oversized entry excluded");
        Check(Text("Ångström Ömega").Matches("öMEGA"), "Case-insensitive Unicode search");
        var snapshot = history.Entries.ToArray();
        var loaded = new History(); loaded.Load(snapshot, 10);
        Check(loaded.Entries.Select(e => e.Id).SequenceEqual(snapshot.Select(e => e.Id)), "Reload order preserved");
        var malicious = new ClipEntry { Kind = ClipKind.Files, Paths = ["relative-path"] };
        Check(!History.IsValid(malicious), "Relative file paths rejected");
        Check(!History.IsValid(new ClipEntry { Kind = ClipKind.Image, Png = [1], Width = 100000, Height = 100000 }), "Image pixel limit");

        var data = new DataObject(); data.SetText("Unicode: ä, ö, € and 🙂\r\nnew line", TextDataFormat.UnicodeText);
        data.SetData(DataFormats.Rtf, "{\\rtf1 test}");
        var text = ClipboardCodec.Read(data, new())!;
        Check(text.Text.Contains('€') && text.Rtf != null, "Unicode and rich text capture");
        var roundtrip = ClipboardCodec.Encode(text);
        Check(roundtrip.TryGetData<string>(DataFormats.UnicodeText, out var encodedText) && encodedText == text.Text, "Text encode preserves contents");
        Check(ClipboardCodec.Read(roundtrip, new()) == null, "Own writes excluded from capture");
        foreach (var format in new[] { "ExcludeClipboardContentFromMonitorProcessing", "application/x-keepassxc-confidential", "Clipboard Viewer Ignore" })
        {
            var privateData = new DataObject(); privateData.SetText("private fixture"); privateData.SetData(format, new MemoryStream([1]));
            Check(ClipboardCodec.Read(privateData, new()) == null, "Private marker " + format);
        }
        var denied = new DataObject(); denied.SetText("private fixture"); denied.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        Check(ClipboardCodec.Read(denied, new()) == null, "Windows history opt-out honored");
        denied.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(1)));
        Check(ClipboardCodec.Read(denied, new()) != null, "Windows explicit opt-in supported");

        using var bitmap = new Bitmap(72, 48);
        using (var graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.Transparent); graphics.FillRectangle(Brushes.MediumSeaGreen, 8, 8, 48, 28); }
        using var imageBytes = new MemoryStream(); bitmap.Save(imageBytes, ImageFormat.Png);
        var imageData = new DataObject(); imageData.SetData("PNG", new MemoryStream(imageBytes.ToArray()));
        var imageEntry = ClipboardCodec.Read(imageData, new())!;
        Check(imageEntry.Width == 72 && imageEntry.Height == 48 && imageEntry.Png!.SequenceEqual(imageBytes.ToArray()), "PNG preserved including alpha");
        Check(ClipboardCodec.Read(imageData, new AppSettings { CaptureImages = false }) == null, "Image capture toggle");
        var imageEncoded = ClipboardCodec.Encode(imageEntry);
        Check(imageEncoded.GetDataPresent("PNG") && imageEncoded.GetDataPresent(DataFormats.Bitmap), "PNG and bitmap offered for paste");
        if (imageEncoded.TryGetData<Bitmap>(DataFormats.Bitmap, out var encodedBitmap)) encodedBitmap.Dispose();
        var fileData = new DataObject(); fileData.SetData(DataFormats.FileDrop, new[] { Path.Combine(output, "fixture.txt") });
        var fileEntry = ClipboardCodec.Read(fileData, new())!;
        Check(fileEntry.Kind == ClipKind.Files, "File drop capture");
        Check(ClipboardCodec.Read(fileData, new AppSettings { CaptureFiles = false }) == null, "File capture toggle");
        var fileEncoded = ClipboardCodec.Encode(fileEntry);
        Check(fileEncoded.TryGetData<MemoryStream>("Preferred DropEffect", out var dropEffect) && BitConverter.ToInt32(dropEffect.ToArray()) == 1, "Files copy rather than repeat a cut");

        var storePath = Path.Combine(output, "test-history.dat");
        var storage = new HistoryStorage(storePath);
        // Simulate a database written by the original app, using its fixed format.
        var legacyClip = Text("Existing history survives the English rename");
        var legacyBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new[] { legacyClip }),
            Encoding.UTF8.GetBytes("Leikehistoria.storage.v1"), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(storePath, legacyBytes);
        Check(storage.Load().Single().Id == legacyClip.Id, "Original encrypted history remains readable after rename");
        string? storageFailure = null; storage.Failed += error => storageFailure = error;
        storage.Schedule([text, imageEntry, fileEntry], true); storage.Flush();
        Check(storageFailure == null && storage.Load().Length == 3, "Encrypted history round-trip");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(storePath)).Contains(text.Text), "History ciphertext hides plaintext");
        for (var i = 0; i < 20; i++) storage.Schedule([Text("state " + i)], true);
        storage.Flush();
        Check(storage.Load()[0].Text == "state 19", "Latest save wins concurrent scheduling");
        storage.Schedule([], false); storage.Flush();
        Check(!File.Exists(storePath), "Disabling persistence removes saved history");
        File.WriteAllBytes(storePath, [1, 2, 3]);
        var rejected = false;
        try { storage.Load(); } catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
        Check(rejected, "Corrupted history rejected"); File.Delete(storePath);
        HistoryRecoveryTests.Run(Check, output);
        PngValidationTests.Run(Check, imageBytes.ToArray());
        var settingsPath = Path.Combine(output, "test-settings.json");
        new AppSettings { MenuCount = 17, HotkeyEnabled = true }.Save(settingsPath);
        Check(AppSettings.Load(settingsPath).MenuCount == 17, "Settings persist");
        Check(AppSettings.Load(settingsPath).HotkeyEnabled, "An existing enabled shortcut preference survives updating");
        File.Delete(settingsPath);

        var pasteFolder = Path.Combine(output, "folder-paste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasteFolder);
        PasteService.Paste(text, new PasteTarget(0, pasteFolder)).GetAwaiter().GetResult();
        PasteService.Paste(imageEntry, new PasteTarget(0, pasteFolder)).GetAwaiter().GetResult();
        Check(File.ReadAllText(Directory.GetFiles(pasteFolder, "*.txt").Single()) == text.Text, "Folder paste writes exact UTF-8 text");
        Check(File.ReadAllBytes(Directory.GetFiles(pasteFolder, "*.png").Single()).SequenceEqual(imageEntry.Png!), "Folder paste writes exact PNG");
        PasteService.Paste(text, new PasteTarget(0, pasteFolder)).GetAwaiter().GetResult();
        Check(Directory.GetFiles(pasteFolder, "*.txt").Length == 2, "Repeated folder paste does not overwrite");
        var attempts = 0;
        var writeFailed = false;
        try { PasteService.WriteUnique(pasteFolder, "write-failure", ".txt", stream => { attempts++; stream.WriteByte(1); throw new IOException("Test disk failure"); }); }
        catch (IOException) { writeFailed = true; }
        Check(writeFailed && attempts == 1 && Directory.GetFiles(pasteFolder, "write-failure*").Length == 0, "Write failure propagates and cleans up without endless retries");
        Check(PasteService.AvailableCopyName(Path.Combine(output, "same.txt"), false).EndsWith("same - copy (1).txt"), "Same-folder duplicate naming");
        Check(Marshal.SizeOf<Native.INPUT>() == (Environment.Is64BitProcess ? 40 : 28), "Native keyboard input ABI");
        var parsed = PasteFromHistory.Program.Parse(["--folder", @"C:\folder with spaces\."], 123);
        Check(parsed.Folder == @"C:\folder with spaces\." && parsed.Target == 123, "Shell path with spaces preserved");


        var visual = new History();
        var samples = new[] { "Right-click → Paste from history", "Copy once. Find it later.", "Coffee, oat milk, sourdough", "Thursday meeting at 14:30", "https://learn.microsoft.com/windows/", "Hi! Here are the details we discussed.", "C:\\Projects\\PasteFromHistory", "A draft for your next message.", "A checklist for the weekend", "The quick menu shows 10 clips by default", "Text, images, and files", "Have a great day!" };
        foreach (var sample in samples.Reverse()) visual.Add(Text(sample), 100);
        visual.Add(imageEntry, 100);
        visual.Add(fileEntry with { Paths = [@"C:\Examples\notes.txt"] }, 100);
        visual.Add(Text(samples[0]), 100);
        using (var full = new HistoryForm(visual, new(), false)) Render(full, "history.png");
        using (var quick = new HistoryForm(visual, new(), true))
        {
            Render(quick, "quick-menu.png");
            var rows = Descendants(quick).OfType<ListBox>().Single();
            Check(rows.Items.Count == 10, "Popup displays exactly ten recent entries");
            var search = Descendants(quick).OfType<TextBox>().Single(t => t.AccessibleName == "Search history");
            search.Text = "great day";
            Check(rows.Items.Count == 1, "Popup search includes older history");
        }
        using (var settingsForm = new SettingsForm(new())) Render(settingsForm, "settings.png");
        using (var emptyForm = new HistoryForm(new(), new(), false)) Render(emptyForm, "empty-history.png");
        using var icon = Theme.CreateIcon(); using var iconFile = File.Create(Path.Combine(output, "PasteFromHistory.ico")); icon.Save(iconFile);
        Check(true, "UI previews and application icon rendered");
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private static void Render(Form form, string filename)
    {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        foreach (var control in Descendants(form)) control.CreateControl();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle with { Width = form.Width, Height = form.Height });
        bitmap.Save(Path.Combine(output, filename), ImageFormat.Png);
    }
}

internal sealed class TestHost : Form
{
    public TestHost()
    {
        Text = "PasteFromHistory – test field"; Size = new Size(850, 550); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 12); Padding = new Padding(24);
        var field = new TextBox { Multiline = true, Dock = DockStyle.Fill, Name = "Test field", AccessibleName = "Test field", ScrollBars = ScrollBars.Vertical,
            Text = "PasteFromHistory test field.\r\n" };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90 };
        for (var i = 1; i <= 12; i++)
        {
            var sample = "Sample clip " + i;
            var button = new Button { Text = "Copy " + i, AutoSize = true, Height = 36 };
            button.Click += (_, _) => Clipboard.SetText(sample);
            buttons.Controls.Add(button);
        }
        Controls.Add(field); Controls.Add(buttons);
        Shown += (_, _) => { field.Focus(); field.SelectionStart = field.TextLength; };
    }
}
