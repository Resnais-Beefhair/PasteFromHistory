# Changelog

## 1.1.0 — 2026-10-12

- Publishable MIT-licensed source project with English contributor documentation
  and an English user guide.
- Rename the app to PasteFromHistory and translate the interface, messages,
  installer entry points, sample clips, and project materials into English.
- Preserve the original encrypted history and Windows integration identities
  for compatibility with earlier local builds.
- .NET 10 LTS, a self-contained Windows x64 EXE, optional per-user installation,
  release ZIP, runtime license notices, and SHA-256 checksums.
- Windows CI and a tag-triggered draft release workflow.
- Tests no longer overwrite the checked-in application icon.
- Explicit Windows Forms designer metadata and updated typed reads for .NET 10.

## 1.0.1 — 2026-10-12

- Keep the original application's right-click menu and text focus when showing
  the history companion. This fixes menus immediately closing in Notepad.
- Locate Notepad's XAML menu and place the companion beside it.
- Restore the original text field before sending the paste command.
- Add native-menu regression checks for activation, focus, and menu visibility.

## 1.0.0 — 2026-10-11

- Local history for Unicode text, HTML/RTF, images, and copied file paths.
- Ten recent entries by default, search, previews, tray settings, and optional shortcut.
- Right-click companion and Explorer/desktop context-menu commands.
- Current-user encrypted storage, privacy markers, and per-user installation.
