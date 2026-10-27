# Security policy

## Supported releases

Security fixes are provided for the latest published release. Older releases
should be updated. Self-contained downloads bundle .NET, so installing a newer
system runtime does not update the runtime inside an older PasteFromHistory EXE.

## Reporting a vulnerability

Use this repository's **Security → Report a vulnerability** feature when enabled.
Do not disclose clipboard contents, exploit details, or personal data in a public
issue. If private reporting is unavailable, open an issue containing only a
request for a private contact channel, without vulnerability details.

Include the application version, Windows version, impact, and steps using
synthetic clipboard data. Do not attach a real history database. There is no
promised response time or paid support agreement.

## Data and trust boundaries

- Clipboard history is local and protected with Windows DPAPI for the current
  user. Another process running as that user can access it; this is not a password vault.
- Capture honors supported clipboard privacy markers. Applications that do not
  set them can place sensitive material into history. Pause capture when needed.
- Optional process exclusions check clipboard owner and foreground app before
  reading content. Unknown sources are skipped when a non-empty list is active.
  Names are exact matches, not verified publisher identities; helpers, browser
  extensions, and renamed programs may use other names. Rules do not erase old clips.
- Source-app recording is separately opt-in and saves only the clipboard owner's
  executable name with the encrypted history. It does not collect window titles,
  document names, or website addresses as source metadata. Turning it off keeps
  existing labels. Names and collections supplied by the user are also stored in
  the encrypted history and share its retention and trust boundaries.
- Optional age expiration applies to unpinned active history, including persisted
  snapshots. Pins and recovery copies are exempt; this is not secure disk erasure.
- Copied file entries store paths, not file contents. HTML/RTF are preserved for
  pasting; the application does not display them in an embedded browser.
- Text transformations run locally and preserve the saved original. HTML
  conversion does not execute scripts or fetch linked resources. URL cleaning
  preserves recognized signing/token parameters; arbitrary custom signature
  schemes cannot all be detected. Review transformed text and links before pasting.
- The app runs as the current user, without elevation or injected DLLs. Windows
  privilege boundaries can prevent pasting into elevated applications.
- There is no telemetry, cloud sync, or background update check. Clicking
  **Settings > About > Check for updates** contacts a fixed GitHub API endpoint
  for release metadata. It sends an application User-Agent but no clipboard
  contents, source names, or settings; GitHub receives normal connection metadata
  such as the request's IP address. Opening the release page is a separate user
  choice. The app does not automatically download or install executables.
- The right-click companion observes mouse events; it does not record typed keys.
  It skips fields marked as passwords by UI Automation. This depends on metadata
  supplied by the destination app and does not identify all sensitive fields.
- Uninstall preserves settings and history. Clear history in the application
  first if you want to remove saved clips. No secure disk erasure is claimed.
- Unreadable history is preserved as a `history.dat.unreadable-*` recovery file.
  These copies retain their original bytes and are never automatically deleted
  by expiration, clearing history, disabling saved history, or uninstalling. They may contain
  sensitive data; keep them private and remove them manually when no longer needed.

Maintainers should enable private vulnerability reporting before publishing the
repository and follow the [release checklist](docs/RELEASING.md).
