using PasteFromHistory.Services;
using PasteFromHistory.Core;

namespace PasteFromHistory;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var foreground = Native.Root(Native.GetForegroundWindow());
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show("The operation failed. " + e.Exception.Message,
            "PasteFromHistory", MessageBoxButtons.OK, MessageBoxIcon.Error);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Compatibility.LegacyName);
        var testOption = Array.IndexOf(args, "--test-data");
        var testMode = testOption >= 0 && testOption + 1 < args.Length;
        if (testMode) data = Path.GetFullPath(args[testOption + 1]);
        if (args.Contains("--remove-integration"))
        {
            try { SystemIntegration.Remove(); return 0; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not remove Windows integration"); return 1; }
        }
        using var instance = new SingleInstance(testMode ? data : "");
        var request = Parse(args, foreground);
        if (!instance.IsFirst)
        {
            if (instance.Send(request)) return 0;
            MessageBox.Show("The app is already running, but could not be reached. Try its system tray icon.", "PasteFromHistory");
            return 1;
        }
        if (request.Action == "quit") return 0;
        try
        {
            Directory.CreateDirectory(data);
            using var app = new TrayApplication(instance, request, data, testMode);
            Application.Run(app);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("PasteFromHistory could not start. " + ex.Message, "PasteFromHistory", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static LaunchRequest Parse(string[] args, nint foreground)
    {
        string action = "history";
        string? folder = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--desktop": action = "folder"; folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); break;
                case "--folder" when i + 1 < args.Length: action = "folder"; folder = args[++i]; break;
                case "--settings": action = "settings"; break;
                case "--popup": action = "popup"; break;
                case "--background": action = "background"; break;
                case "--quit": action = "quit"; break;
            }
        }
        return new LaunchRequest(action, folder, (long)foreground);
    }
}
