# PasteFromHistory user guide

PasteFromHistory keeps copied text, images, and file references close at hand.
It runs in the Windows system tray and offers a **Paste from history** companion
beside other applications' right-click menus.

## Download and install

Download the latest `PasteFromHistory-<version>-win-x64.zip` from
[GitHub Releases](https://github.com/Resnais-Beefhair/PasteFromHistory/releases/latest).
GitHub's automatic **Source code** archives contain development files, not the
ready-to-run app. Extract the complete release ZIP before using it.

Double-click **Install.cmd** to install for the current Windows user in
`%LOCALAPPDATA%\Programs\PasteFromHistory`, create a Start menu shortcut, and
launch the app. Administrator access is not required. The .NET 10 runtime is
included, so no separate runtime installation is needed.

You can also run **PasteFromHistory.exe** directly. Keep it in a permanent
folder: Explorer menu commands use its executable path. First launch enables
the default right-click integration. The app does not download anything.

The target is Windows 11 x64. Releases are currently unsigned, so Windows may
show an unknown publisher message. SHA-256 checksums are provided with each
release; compare with `Get-FileHash <download> -Algorithm SHA256` if needed.

The mint-green clipboard icon appears near the clock. Windows may place it
inside the **Show hidden icons** arrow. Drag it to the visible tray if desired.

## Open and paste

| Action | What happens |
| --- | --- |
| Ordinary right-click in another app | A **Paste from history** companion appears beside the app's menu. Click it, then choose a clip. |
| Right-click the desktop or an Explorer folder | **Paste from history** opens the picker for that folder. On Windows 11, it may be under **Show more options**. |
| Ctrl+Shift+V | Open the quick picker for the current app when enabled in Settings. Off by default in new profiles. |
| Left-click the tray icon | Open the full history, search, and preview. |
| Right-click the tray icon | Settings, pause/resume capture, clear history, and quit. |

The quick menu shows **10 recent clips** by default. The full history holds up
to **100 clips** by default. Both limits can be changed in Settings. Search can
find older clips outside the ten most recent items.

The shortcut is system-wide when enabled and can override another application's
own Ctrl+Shift+V action, such as plain-text paste. Windows only reports a conflict
when global registration fails; it does not detect every app-local binding.
Uncheck **Open the quick menu with Ctrl+Shift+V** in Settings and click **Save**
to release the keys. Updating preserves an existing saved shortcut preference.

In the quick picker, a single click or Enter pastes the selected clip. Alt+1-9
selects a row; Escape closes the picker. In the full history, select a row and
click **Paste**, or double-click it. **Copy** only restores it to the clipboard.

The companion is a separate, non-activating window. It does not inject a row
into every app's internal menu. Normal right-click behavior is preserved.
Custom menus, fullscreen apps, games, and apps running as administrator may
behave differently. The shortcut and tray are alternative ways to open history.

## Mouse monitoring

When **Show companion on ordinary right-click** is enabled in Settings, the app
installs a global low-level Windows mouse hook (`WH_MOUSE_LL`). It observes
right-button presses/releases and left/middle-button presses, their positions,
and the destination window/focus so it can place or dismiss the companion and
restore the paste target. Mouse events continue to the original application.

The hook does not record typed keys or keep a production mouse-event log.
Turn off **Show companion on ordinary right-click** and click **Save** to remove
the hook. Clipboard capture, the tray, the optional shortcut, and the separately
configured Explorer command remain available. Pausing clipboard capture alone
does not disable the companion's mouse monitoring.

## Captured content

- Unicode text, including available HTML and RTF formatting.
- Images stored as PNG, with a bitmap representation offered when pasted.
- Paths to copied local files and folders. File contents are not backed up;
  originals must still exist when you paste them.
- Repeated content moves to the top. Different text formatting remains distinct.

Capture starts with new copies after launch. It does not import Windows Win+V
history. Each clip is limited to 8 MiB, images to 40 million pixels, and all
history content to 48 MiB. Old clips are removed when the count or size limit is
reached. App-specific clipboard objects are not all supported.

Pasting into a folder creates a new UTF-8 TXT file for text or PNG file for an
image. Files are copied using Windows copy dialogs; history never repeats an
old cut operation. Existing text/image files are not overwritten. If automatic
paste into another app is blocked, the clip remains available for manual Ctrl+V.

## Settings and privacy

History is encrypted with Windows DPAPI for the current user and stored at
`%LOCALAPPDATA%\Leikehistoria\history.dat`. Settings are in `settings.json` beside
it. This legacy directory name is retained so existing local versions remain
compatible. Other programs running as the same Windows user can access this
history; it is not a password vault.

There is no network connection, cloud sync, telemetry, or automatic update
service. Capture honors `ExcludeClipboardContentFromMonitorProcessing`,
`CanIncludeInClipboardHistory=0`, `application/x-keepassxc-confidential`, and
`Clipboard Viewer Ignore`. Apps do not always mark sensitive copies. Pause
capture from the tray when needed; existing clips remain usable while paused.

Turning off saved history deletes the saved history file, while the current
session's history stays in memory. **Clear history** removes saved clips but
leaves the current Windows clipboard unchanged. Start with Windows is off by default.

## Unreadable history

If saved history cannot be read, the app first attempts to move the original to
`history.dat.unreadable-<UTC timestamp>-<unique ID>` in the same data directory.
The original bytes are preserved without overwriting an earlier recovery file.
A startup notification gives the saved path, and new clips start a new history.

If the original cannot be moved (for example, another program has locked it),
the app captures new clips in memory but blocks all history-file writes and
deletion for that session, including on exit. Those new clips will not survive
exit. Resolve the access problem and restart the app to retry recovery.

Recovery files are not automatically repaired, restored, or deleted. **Clear
history**, disabling saved history, and uninstalling do not remove these copies.
They can contain sensitive clipboard data; keep them private and remove them
manually when no longer needed. Do not attach them to public issues. A changed
Windows encryption key may prevent recovery even when the original file exists.

## Update or remove

Extract a new release ZIP and run its **Install.cmd**. The installer asks the
running app to save and exit before updating its program files. Settings and
history are preserved. Updates are installed manually.

When upgrading from the original **Leikehistoria** local build, quit and uninstall
that version first, then install PasteFromHistory. The original uninstaller
preserves your history, and the renamed app reads the same encrypted format.
Do not run the old uninstaller after installing the new app, because they share
compatible registry identities.

Run **Uninstall.cmd** in the installed app's folder to remove the program,
its Start menu shortcut, Explorer commands, and automatic startup entry.
History and settings are preserved. Clear history in the app first if you also
want to remove saved clips.

For a directly run copy, quit it and remove its Windows integration with:

```powershell
.\PasteFromHistory.exe --remove-integration
```

Registry changes are current-user only. The compatible shell keys are under
`HKCU\Software\Classes\{DesktopBackground,Directory\Background,Directory,Drive}\shell\Leikehistoria.v1`.
Optional startup uses the `Leikehistoria` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Source and license

The source, contribution guide, and issue tracker are at
[Resnais-Beefhair/PasteFromHistory](https://github.com/Resnais-Beefhair/PasteFromHistory).
The application is MIT-licensed. The ZIP includes its license and the bundled
Microsoft runtime notices in `licenses/`.
