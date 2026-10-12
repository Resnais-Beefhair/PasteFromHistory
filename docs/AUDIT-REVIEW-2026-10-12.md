# Review of the 12 October 2026 external audit

The 21 proposals in `PasteFromHistory-issues.md` were checked against the
published 1.1.0 source and the repository settings. Proposed implementations
were treated as suggestions, not as a requirement to add every feature.

## Audit fixes (shipped in 1.2.0)

These fixes were prepared as 1.1.1, which was not published separately. The
original assessment below records the audit's scope. After that review, the
maintainer requested five feature proposals for 1.2.0; see the follow-up below.

- Preserve unreadable history before any new save, including the save on exit.
  If preservation fails, block both writes and deletion for that session. Reject
  invalid decrypted records, null arrays/records, and duplicate IDs without
  discarding the original file. Opening the file directly also distinguishes an
  access error from an absent file.
- Read PNG dimensions before passing data to an image decoder. Use the same
  40-million-pixel cap for PNG capture, bitmap capture, saved-image validation,
  previews, and pasting. Saved dimensions must agree with the PNG header.
- Make the global shortcut opt-in for new profiles. Preserve explicit settings
  from existing profiles, and document the difference between an application
  shortcut and a registered global shortcut.
- Document mouse monitoring and recovery-file retention; separate quick builds
  from release packaging instructions. Ignore recovery files in Git.

## Disposition of each proposal

| # | Assessment | Decision and reason |
| --- | --- | --- |
| 01 | Confirmed data-loss bug | Fixed. The overwrite could occur on exit even without another copied clip. A rename alone was insufficient: failure to preserve the original must also block subsequent saves and deletion. Recovery files get unique names and are never overwritten. |
| 02 | Useful privacy feature | Deferred. Process exclusions could add protection, but a process-name list is not a guarantee against password capture. Unknown owners, helper processes, and browser-hosted tools need a defined policy. The proposed fail-open fallback would still capture unidentified sources. Existing privacy markers and pause capture remain documented. `GetWindowThreadProcessId` already exists in `Native`. |
| 03 | Real shortcut compatibility concern | Mitigated: the shortcut is now off by default for new profiles, and the guide explains how to release it. A global registration can take precedence over an app-local binding without reporting a registration conflict. A customizable shortcut editor is a separate feature and is deferred. Existing saved choices are preserved. |
| 04 | Optional retention policy | Deferred. Current retention is bounded by count and bytes and is documented; it has no age limit. Expiration is useful but changes data-retention behavior. It needs clear rules for duplicate promotion, persisted deletion, and any future pinned clips. |
| 05 | Optional uninstall behavior | Deferred. Preserving data on uninstall is deliberate and documented. The guide now also explains recovery copies. A future `-RemoveData` option must protect against links at every relevant path component and retain an explicit opt-in. No user's data was removed as part of this review. |
| 06 | Test-infrastructure enhancement | Deferred. The existing STA harness is executed by CI and the packaging script. Moving to xUnit does not remove the unlocked-desktop requirement for native-menu tests. Focused regression cases were added to the existing harness; a framework/reporting migration can be reviewed separately. |
| 07 | Useful development hardening | Deferred. CodeQL would add static analysis, but its absence does not establish a defect in the application. It needs its own workflow and permission review. No scheduled job was added by this audit fix. |
| 08 | Useful distribution hardening | Deferred. Provenance attestation and Authenticode signing address different questions. Neither should be represented as a current feature, and checksums are not a publisher identity. Releases remain explicitly unsigned. Selecting a signing service and credentials requires a separate maintainer decision. |
| 09 | Mostly presentation choices; one item already addressed | Live GitHub verification returned private vulnerability reporting **enabled**. The listed three topics, empty homepage, and disabled Discussions were confirmed; they do not block cloning, contribution, or downloads. Discussions and a social preview are optional, not correctness requirements. |
| 10 | Confirmed resource-safety gap | Fixed and extended to restored images, previews, and paste. A compressed-byte limit alone does not bound pixel dimensions before decoding. The lightweight PNG header check is a size guard, not a replacement for the image decoder's validation. Bitmap objects can already be allocated by Windows/another application before the app receives them. |
| 11 | Unproven optimization | Deferred. The history is capped at 1,000 entries and 48 MiB. A cached byte total adds consistency obligations to every mutation; there is no measured bottleneck justifying that complexity here. |
| 12 | Optional support feature | Deferred. Production diagnostic logging would require a privacy review and bounded storage. Current `Trace` output is restricted to `--test-data` mode. Full exception messages and stack traces must not automatically be assumed free of private paths or content. |
| 13 | Deliberate compatibility choice | Keep the existing data path and encryption/integration identities. Renaming a folder is cosmetic and complicates upgrades, older installations, and rollback. The English documentation already explains the legacy directory. A migration needs explicit compatibility tests and a recovery plan. |
| 14 | Valid documentation gap | Fixed. The guide names the global mouse hook, observed buttons/positions and focus, and the exact setting that removes it. It also distinguishes pausing clipboard capture from disabling mouse monitoring. |
| 15 | Partly valid documentation cleanup | Build instructions are now separated. A code of conduct is a maintainer policy with reporting and enforcement commitments; no private contact address or policy commitment was invented for this patch. |
| 16 | New feature | Deferred. Pinning needs defined count/byte budgets. Simply skipping pinned entries in `Trim` could defeat the memory/storage cap; a pin limit of 50 alone would not preserve the existing 48 MiB limit. |
| 17 | New feature | Deferred. Plain-text paste is useful but intentionally differs from the current format-preserving paste. Its shortcut, mouse gesture, UI affordance, and image/file behavior need a coherent design and tests. |
| 18 | New feature, outside the chosen language scope | Deferred. English was explicitly selected for the app and project. Finnish localization can be added later without changing that project's current language choice implicitly. |
| 19 | New security-sensitive feature | Deferred. Cross-user encrypted export/import needs a versioned format, KDF parameters, authenticated metadata, limits on imported data, and failure handling. It is not a small repair to local DPAPI storage. |
| 20 | New distribution channel | Deferred. The GitHub release is already downloadable. Submitting a package to another repository and maintaining its updates is a separate distribution decision. |
| 21 | New platform target | Deferred. Cross-compilation alone does not validate native calls, hooks, installation, or runtime startup on ARM64. No untested ARM64 binary is presented as supported. |

## Validation

On Windows x64 with .NET SDK 10.0.401, all **78 base regression checks** and
**17 interactive native-menu checks** passed. The Release build completed with
zero warnings and errors. The 1.1.1 self-contained package passed the file
allowlist, executable-version, and isolated-startup smoke checks. This does not
replace the fresh-machine and third-party application manual release matrix.

The regression suite covers successful recovery, repeated recovery, denied
access, failed moves, continued write/delete blocking, malformed decrypted
content, and application startup followed immediately by exit. PNG fixtures
exercise bad signatures, short or malformed IHDR chunks, zero/out-of-range
dimensions, excessive pixel counts, arithmetic bounds, and forged saved metadata.
Existing clipboard, storage, folder-paste, UI-rendering, and native-menu tests
remain in place. All fixtures are synthetic and use isolated test directories.

## 1.2.0 feature follow-up

The maintainer subsequently approved proposals **02, 03, 04, 16, and 17**:

- **02: process exclusions** now check both clipboard owner and foreground process
  before reading content. Unknown sources are skipped when rules are configured;
  helper/browser limitations and future-copy scope are documented.
- **03: configurable shortcut** adds validated keys and modifiers. Replacement
  registration happens before releasing the old keys; conflicts retain the old
  binding. New profiles still default to the shortcut being disabled.
- **04: optional age expiration** defaults off and applies to unpinned clips.
  Duplicate capture refreshes age; sweeps persist deletions across restart.
- **16: pins** have a 50-entry / 16 MiB budget within the 48 MiB total and survive
  automatic eviction. Explicit delete/clear still removes pins.
- **17: plain-text paste** strips HTML/RTF for one paste without changing the saved
  clip. The button and Shift gestures are documented, with normal image/file behavior.

All five are implemented in 1.2.0. The remaining deferred proposals retain their
original disposition. See [1.2.0 validation](TESTING.md#120-local-release-validation)
for the expanded 138 base / 35 interactive checks and remaining coverage gaps.

## Technical references

- [PNG signature, chunk structure, and IHDR dimensions](https://www.w3.org/TR/png-3/#11IHDR).
- [File.Move and its overwrite behavior](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0).
- [RegisterHotKey system-wide registration and conflict behavior](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

The GitHub settings check was performed on 12 October 2026 using the repository
metadata and private-vulnerability-reporting endpoints. It is a dated observation,
not a promise that repository settings can never change.
