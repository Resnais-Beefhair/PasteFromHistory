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
- Copied file entries store paths, not file contents. HTML/RTF are preserved for
  pasting; the application does not display them in an embedded browser.
- The app runs as the current user, without elevation, injected DLLs, network
  requests, telemetry, or a cloud service. Windows privilege boundaries can
  prevent pasting into elevated applications.
- The right-click companion observes mouse events; it does not record typed keys.
- Uninstall preserves settings and history. Clear history in the application
  first if you want to remove saved clips. No secure disk erasure is claimed.
- Unreadable history is preserved as a `history.dat.unreadable-*` recovery file.
  These copies retain their original bytes and are never automatically deleted
  by clearing history, disabling saved history, or uninstalling. They may contain
  sensitive data; keep them private and remove them manually when no longer needed.

Maintainers should enable private vulnerability reporting before publishing the
repository and follow the [release checklist](docs/RELEASING.md).
