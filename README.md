# PasteFromHistory

A local clipboard history app for Windows, with an English interface. Copy text,
images, or files and reuse them from the tray, a keyboard shortcut, or **Paste from history** beside an application's right-click menu.

**Windows x64 · C# / .NET 10 LTS · MIT · No cloud or telemetry**

[User guide](docs/USAGE.md) · [Contributing](CONTRIBUTING.md) ·
[Architecture](docs/ARCHITECTURE.md) · [Release guide](docs/RELEASING.md)

![History window with synthetic example clips](docs/images/history.png)

## Get the app

Open [Releases](https://github.com/Resnais-Beefhair/PasteFromHistory/releases/latest) and download
`PasteFromHistory-<version>-win-x64.zip`. Extract it and either:

- Run `PasteFromHistory.exe` directly from a permanent folder.
- Run `Install.cmd` to install for your Windows account and add a Start menu shortcut.

The release includes the .NET runtime. No SDK, separate runtime installation, or
administrator access is required. The ZIP also includes the user guide,
uninstaller, and license notices. GitHub's automatic **Source code** downloads
are for developers and do not contain the EXE.

The target is Windows 11 x64. Windows 10 22H2 may work, but is not part of the
current manual validation. ARM64 and x86 downloads are not provided. The UI is
in English. Builds are unsigned, so Windows may display an unknown
publisher warning. Release assets include SHA-256 checksums; these detect a
changed download but do not replace code signing.

## Use it

| Action | Result |
| --- | --- |
| Right-click in another app | A **Paste from history** companion button appears near its menu. Choose it, then a clip to paste. |
| Right-click a desktop or Explorer folder | An actual **Paste from history** shell command pastes into that folder. Windows 11 may put it under **Show more options**. |
| Ctrl+Shift+V | Open recent history for the current application; optional in settings. |
| Left-click the tray icon | Browse and search the full history. |
| Right-click the tray icon | Settings, pause capture, clear history, or quit. |

The popup shows **10 recent clips** by default, with **100 clips** retained in the
full history. Both limits are configurable. Capture starts with new copies after
the app launches; it does not import the Windows Win+V database.

**The companion is a separate, non-activating window.** It does not inject a menu
item into every application's private menu implementation. Ordinary right-click
behavior is preserved. Native Win32 and Notepad XAML menus have position
handling; custom menus, games, fullscreen windows, and elevated applications can
behave differently. See [compatibility testing](docs/TESTING.md).

## What is stored

- Unicode text and available HTML/RTF formatting.
- Images as PNG, with bitmap compatibility when pasted.
- Copied file and folder paths. Original files must still exist; their contents
  are not backed up in history. Pasting from history copies, rather than repeats a cut.

Explorer pastes create a new TXT or PNG file, or copy files using Windows shell
copy dialogs. Same-named text and image files are not overwritten. Each clip is
limited to 8 MiB and total content to 48 MiB; older clips are evicted as needed.
Application-specific clipboard formats are not all preserved.

History is encrypted with current-user Windows DPAPI in
`%LOCALAPPDATA%\Leikehistoria\history.dat`; settings are beside it. This legacy
folder name is retained for compatibility with earlier local builds. Other programs
running as the same Windows user can still access this data. Capture respects
supported privacy markers, but applications do not always mark sensitive data.
Pause capture when needed. See [SECURITY.md](SECURITY.md) for details.

Uninstall with `Uninstall.cmd`. It removes the app's integration and program files,
but preserves history/settings. Clear history in the app first if you want to
remove saved clips. Automatic startup is optional and off by default.

## Build from source

Install Git and the latest stable [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
on Windows. Clone the repository, then build from its root:

```powershell
git clone https://github.com/Resnais-Beefhair/PasteFromHistory.git
cd PasteFromHistory
dotnet build PasteFromHistory.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

`Build.ps1` runs regression tests, publishes a self-contained EXE, collects the
runtime licenses, creates the ZIP and checksums, and verifies the packaged EXE.
The outputs are `dist/PasteFromHistory-<version>-win-x64.{exe,zip}` and a
`-SHA256SUMS.txt` file. Version numbers come from `Version.props`.

The app has no third-party NuGet application dependencies. The first release
build downloads Microsoft's runtime packs from nuget.org. `global.json` selects
a stable .NET 10 SDK; build outputs and caches stay out of Git.

For development without changing your normal history or Explorer registration:

```powershell
dotnet run --project PasteFromHistory.csproj -- --test-data "$PWD\artifacts\dev-profile"
```

This instance still observes live clipboard changes. See [CONTRIBUTING.md](CONTRIBUTING.md)
for isolated testing, the STA test harness, and the interactive menu regression.

## Project map

| Path | Purpose |
| --- | --- |
| `Core/` | Settings, clip model, limits, and encrypted history storage |
| `Services/` | Clipboard conversion, native Windows calls, paste, and integration |
| `UI/` | History window, settings, companion button, and theme |
| `TrayApplication.cs` | Application lifetime and tray orchestration |
| `tests/` | Regression harness, fixture window, and native-menu tests |
| `scripts/` | Build, verification, installation, and removal |
| `.github/` | Windows CI, draft releases, and contribution templates |

Contributions in English are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md)
and open an issue or pull request with reproducible steps. Releases and the
project's current limitations are recorded in [CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE). Bundled runtime components retain their own licenses; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
