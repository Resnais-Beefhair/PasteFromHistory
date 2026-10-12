using Microsoft.Win32;
using PasteFromHistory.Core;

namespace PasteFromHistory.Services;

internal static class SystemIntegration
{
    internal const string VerbName = Compatibility.LegacyName + ".v1";
    private static readonly (string Path, string Argument)[] Locations =
    [
        (@"Software\Classes\DesktopBackground\Shell\" + VerbName, "--desktop"),
        (@"Software\Classes\Directory\Background\shell\" + VerbName, "--folder \"%V\\.\""),
        (@"Software\Classes\Directory\shell\" + VerbName, "--folder \"%1\\.\""),
        (@"Software\Classes\Drive\shell\" + VerbName, "--folder \"%1\\.\"")
    ];

    public static void SetExplorerMenu(bool enabled)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The application path could not be found.");
        foreach (var (path, argument) in Locations)
        {
            if (!enabled) { Registry.CurrentUser.DeleteSubKeyTree(path, false); continue; }
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("MUIVerb", "Paste from history");
            key.SetValue("Icon", "\"" + exe + "\",0");
            using var command = key.CreateSubKey("command");
            command.SetValue("", "\"" + exe + "\" " + argument);
        }
        Native.SHChangeNotify(0x08000000, 0, 0, 0);
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue(Compatibility.LegacyName, "\"" + Environment.ProcessPath + "\" --background");
        else key.DeleteValue(Compatibility.LegacyName, false);
    }

    public static void Remove() { SetExplorerMenu(false); SetStartup(false); }
}
