# Architecture

PasteFromHistory is a Windows Forms application targeting `net10.0-windows`.
`Program.Main` owns the STA thread, captures the original foreground window,
chooses the data directory, and establishes a single-instance scope.
`TrayApplication` owns the message loop and coordinates the services and forms.

## Capture and storage

`MessageWindow` listens to `WM_CLIPBOARDUPDATE`. A short timer retries clipboard
access when another application holds it. `ClipboardCodec` checks privacy
markers before decoding text, PNG/bitmap, or file-drop data. App-owned writes
carry a private marker to prevent capture loops.

`ClipboardSource` snapshots the clipboard sequence, owner process name, and
foreground process name at notification time. `CaptureExclusions` checks exact,
normalized names before the data provider is called. Non-empty exclusion lists
reject unknown sources; invalid rules block capture. The sequence is checked
before and after reads; a changed clipboard queues a fresh source decision.
Process names are transient metadata, not stored with history entries.

`History` validates entries, deduplicates equal content, promotes recent clips,
and applies count and byte limits. `HistoryStorage` serializes snapshots to JSON,
encrypts them using DPAPI `CurrentUser`, and replaces the history file via a
temporary file. Pending writes are serialized and flushed during normal exit.
Settings are separate JSON; do not add clip content or diagnostics to settings.

Pinned entries are limited to 50 / 16 MiB within the 48 MiB total. Automatic
trimming removes unpinned entries only, applying optional UTC age expiration,
the unpinned count limit, and then the total byte budget. Pin admission checks
both budgets before changing an entry. Duplicate capture preserves ID and pin
but refreshes capture time. Unpinning immediately reapplies trimming.
`TrayApplication` sweeps at startup, every minute, before opening/choosing, after
settings changes, and on exit. Changes schedule persistence, including startup
expiration. Unreadable originals and recovery copies remain protected.

## Opening and pasting

The tray, optional global shortcut, and shell verbs open `HistoryForm`.
`GlobalHotkey` reserves a replacement combination before releasing the old one;
failed registration leaves the old binding intact. Alternate registration IDs
prevent queued messages from the previous binding opening a popup after a change.
`HotkeyGesture` shares supported keys and labels between settings and tests.
`MouseObserver` uses a low-level mouse hook to observe ordinary right-clicks and
record the original target and focused control before a menu can move focus.
The hook passes events on. On mouse-down, `EditableTargetProbe` checks the clicked
UI Automation element on an MTA worker. Only enabled, focusable Edit/Document
controls with explicit writable metadata qualify; read-only flags veto the result.
Text runs may be walked up to their editor, but buttons and other controls may not.
The probe never reads text/value contents, never falls back to a different focused
field, and allows at most one provider call in flight. A 600 ms deadline fails
closed without blocking input or creating a backlog if a provider hangs.

A timer waits for the native menu to open and for a successful probe before showing
`MenuCompanion`. A new click, right-button drag, settings/history action, closed
observed menu, or changed foreground invalidates the request. No UI Automation
objects cross threads. See Microsoft's [UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading).

The companion is a separate no-activate window. Native extended styles keep it
above the menu without WinForms' managed `TopMost` visibility path activating
it. Do not replace this with `Form.TopMost = true`: that previously closed
Notepad's menu. `Native.FindMenu` recognizes Win32 and relevant XAML popup classes;
`CompanionPlacement` chooses a nearby position that avoids a recognized menu and
clamps to the current monitor's work area. Unknown menus use a small gap above the
click instead of a fixed horizontal offset. The hidden window first moves to the
target monitor so its current DPI is applied before placement.

Choosing a clip closes the picker, restores the original application's focus,
writes the formats to the clipboard, and uses `SendInput` for Ctrl+V. Focus
restoration validates the window/control relationship and only temporarily
attaches input threads. Clipboard contents remain available for manual paste if
automatic paste is blocked. Folder targets instead create text/image files or
invoke Windows file copy operations.

Plain-text paste passes a flag through `HistoryForm` and `PasteService` to
`ClipboardCodec.Encode`: it offers Unicode text and the own-write marker without
HTML/RTF. It does not mutate the stored entry and rejects non-text entries.

## Windows integration and lifetime

`SystemIntegration` writes only current-user registry entries: four Explorer
shell verbs and an optional Run value. It uses `Environment.ProcessPath`, which
works with the self-contained single-file release. No DLL is injected into other
processes and the app does not require elevation.

`SingleInstance` uses a local mutex and a current-user named pipe scoped by user,
session, and optional test directory. Secondary launches send a small request to
the active instance, including `--quit` for orderly shutdown before updates.

`--test-data <absolute path>` gives development instances their own history,
settings, and instance scope. It skips persistent Explorer/startup integration,
but still observes live clipboard and mouse events. Test profiles are private.

The public name is PasteFromHistory. `Core/Compatibility.cs` deliberately retains
the original local build's storage directory, DPAPI entropy, clipboard marker,
IPC prefix, and registry identity. Keep these compatible when changing branding;
the regression suite includes an original-format encrypted history fixture.

## Useful starting points

| Change | Start here |
| --- | --- |
| History ordering, capacity, deduplication | `Core/History.cs`, `tests/Program.cs` |
| Clipboard formats and privacy | `Services/ClipboardService.cs` |
| Process exclusions | `Core/CaptureExclusions.cs`, `Services/ClipboardSource.cs` |
| Pins, expiration, feature regression coverage | `Core/History.cs`, `tests/FeatureTests.cs` |
| Shortcut settings and registration | `Core/HotkeyGesture.cs`, `Services/GlobalHotkey.cs` |
| Right-click compatibility | `Services/MouseObserver.cs`, `UI/MenuCompanion.cs`, `Services/Native.cs` |
| Paste focus or folder output | `Services/PasteService.cs`, `tests/MenuCompanionTests.cs` |
| Native shortcut, exclusion, and rich-text integration | `tests/FeatureInteractionTests.cs` |
| Tray, launch behavior, settings application | `TrayApplication.cs` |
| Packaging and runtime updates | `Version.props`, `scripts/Build.ps1` |

## API references

- [Clipboard change notifications](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)
- [Clipboard ownership](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardowner)
- [Clipboard sequence numbers](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber)
- [Global shortcuts and reserved keys](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Clipboard privacy formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats)
- [SendInput and privilege restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [Shell context-menu commands](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu-handlers)
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
