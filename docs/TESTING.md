# Testing and compatibility

## Automated checks

Run `scripts/Test.ps1` on Windows. The STA test harness covers history limits,
ordering, duplicate handling, privacy flags, Unicode/rich text, images, file-drop
formats, encrypted storage, save scheduling, folder output, and popup limits.
It renders the UI with synthetic clips into `artifacts/tests/`. These are render
previews, not proof that all interactions work.

Run `scripts/Test.ps1 -Interactive` from an unlocked desktop for native menu
checks. They open a real Win32 menu loop three times and check that the companion
preserves the menu, foreground window, and text focus, then check restoration
and invalid targets. The test briefly activates its own form. CI runs the base
suite; the interactive suite requires a manual desktop session.

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
