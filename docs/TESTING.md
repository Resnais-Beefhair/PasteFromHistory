# Testing and compatibility

## Automated checks

Run `scripts/Test.ps1` on Windows. The STA test harness covers history limits,
ordering, duplicate handling, privacy flags, Unicode/rich text, images, file-drop
formats, encrypted storage, save scheduling, folder output, and popup limits.
It renders the UI with synthetic clips into `artifacts/tests/`. These are render
previews, not proof that all interactions work.

Recovery regressions use synthetic encrypted files and locked test files. They
also start and immediately stop isolated application contexts with the mouse
hook, hotkey, and Explorer integration disabled. PNG regressions cover truncated
and malformed headers, excessive dimensions, integer bounds, and mismatched saved
metadata before decoding. No real history database is read or changed.

Run `scripts/Test.ps1 -Interactive` from an unlocked desktop for native menu
checks. They open a real Win32 menu loop three times and check that the companion
preserves the menu, foreground window, and text focus, then check restoration
and invalid targets. They also register and replace actual Windows shortcuts,
exercise registration conflicts and key release, paste plain and rich text into
a separate rich-text test process, and check process exclusions through the real
clipboard listener. They temporarily activate test windows and replace the current
clipboard with synthetic text; they do not read or restore its previous contents.
CI runs the base suite; the interactive suite requires a manual desktop session.

`FeatureTests` covers old settings/history compatibility, pin identity and storage,
count/byte limits, exact expiration boundaries, duplicate capture age, persisted
startup/periodic deletion, plain-text formats, shortcut rollback, source exclusions
before data access, and the settings/history UI actions. Rendered fixtures include
the three settings tabs and the history window at a smaller size.

`scripts/Build.ps1` additionally verifies the exact release file list, EXE
version, and startup with an isolated `--test-data ... --quit` request. This
smoke test loads the bundled runtime and Windows Forms without contacting the
normal running instance. It does not replace a full installation/paste test.

`dotnet test` alone does not run this project's custom regression executable.

## Manual release matrix

Use invented text, small sample images, and temporary files. Record the Windows
build, application version, display scale, and result in the release notes.
Do not publish private clipboard contents or actual history files.

| Area | Check |
| --- | --- |
| Fresh system | Extract the release on Windows x64 without .NET installed; start and quit. |
| Installation and upgrade | Install without elevation, verify Start menu shortcut; update with history present and confirm it survives. |
| Notepad | Open right-click menu; keep it open for several seconds; select history and paste into the original field. |
| Browser text field | Repeat in a browser input and a multiline field; verify the original menu still works. |
| Explorer and desktop | Paste text, image, and copied files; check filename conflicts and cancellation. |
| Tray and shortcut | Open full history and settings, pause/resume, search, clear, quit; check occupied Ctrl+Shift+V. |
| Custom shortcut | Change keys; confirm old keys are released. Try an occupied combination; confirm the old shortcut still works. Disable and restart. |
| Plain text | Paste formatted Unicode text normally and using the button, Shift+Enter, and Shift+click into an editor and browser. Images/files keep their normal behavior. |
| Pins | Pin/unpin, filter, recopy, restart, fill recent history, reach pin count/size limits, delete, and clear. |
| Expiration | Enable an age limit, restart with old synthetic clips, wait for a sweep, and unpin an expired clip. Verify pins survive and expired clips do not return. |
| Excluded apps | Exclude the clipboard owner and foreground app separately; test a helper process, unknown owner, empty list, and a browser. Existing clips remain. |
| Multiple displays | Place menus near each screen edge at 100%, 150%, and 200% scaling. |
| Persistence | Restart, disable saved history, re-enable, and restart again. |
| Privacy | Copy content from an app that sets a supported exclusion marker; confirm it is not captured. |
| Removal | Uninstall and verify the app's shell/Run entries are removed and saved history is preserved. |

## Known boundaries

- The app targets Windows 11 x64. Other Windows editions/architectures are not
  currently covered by the manual validation.
- The companion is an adjacent window, not a universally injected menu item.
  Custom menus can have unusual placement or dismiss behavior. Report the exact
  application/version with reproducible steps.
- Elevated applications, secure desktops, games, and fullscreen windows can
  restrict hooks, focus changes, or simulated paste.
- File history stores references: moving or deleting originals can make an
  entry unusable. Arbitrary app-specific clipboard objects are not retained.
- A successful base test suite does not establish compatibility with every app.

## 1.2.0 local release validation

On 12 October 2026, Windows 11 x64 build **26300.9457 (26H2)** with .NET SDK
**10.0.401** passed **138 base checks** and **35 interactive checks**. The full
release build passed with zero warnings/errors, including package allowlist,
EXE version, and isolated startup with its bundled runtime. Synthetic screenshots
were inspected for all settings tabs and the history actions.

The native tests use a real Win32 menu and a separate Windows Forms rich-text
process. The complete third-party application matrix (including current Notepad
and browser versions), fresh-machine installation without .NET, and the multi-DPI
matrix were not rerun for 1.2.0. Those remain coverage gaps, not passing results.
