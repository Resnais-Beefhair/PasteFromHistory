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

The owned-menu regression creates actual native `Net UI Tool Window` fixtures.
It checks document ownership, closest-menu discovery, adjacent mini-toolbar
bounds, foreground preservation and disappearance, and rejects both an unrelated
owned dialog and a recognized menu owned by that dialog. It does not substitute
for testing Word itself.

`EditorTargetTests` checks fail-closed metadata, unavailable providers, bounded
timeouts/concurrency, and placement at 100/150/200% sizes on positive and negative
monitor coordinates. Its interactive checks use real writable, read-only and
disabled native text controls, a rich-text document, buttons, static labels and
a game-like drawing panel. They verify that a focused editor elsewhere does not
make the clicked surface eligible and that right-click captures the clicked field.
The base suite also checks Word menu/mini-toolbar placement geometry and that
diagnostic failures neither suppress a valid editor nor log provider exception
messages that might contain private text.

`FeatureTests` covers old settings/history compatibility, pin identity and storage,
count/byte limits, exact expiration boundaries, duplicate capture age, persisted
startup/periodic deletion, plain-text formats, shortcut rollback, source exclusions
before data access, and the settings/history UI actions. Rendered fixtures include
all four settings tabs and the history window at its DPI-scaled minimum size.

`SearchMetadataTests` covers names/collections, old-history migration, metadata
budgets, duplicate retention, optional source attribution, search ranking and
bounded typo matching over 1,000 clips near the 48 MiB history limit.
`HistoryBrowserTests` exercises combined filters, Recent/Favorites separation,
the search debounce, and modal cancellation in the quick picker.
`TextTransformTests` covers Unicode, URL query preservation, recognized signed
URLs, HTML/CF_HTML UTF-8 selection offsets, malformed input, bounded expansion,
and editable previews. `ReleaseUpdateTests` validates stable release parsing and
the fixed repository destination without requiring network access.

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
| Word | Right-click document text, preserve its context menu and mini toolbar, and paste Unicode into the original caret. Check the title/tab strip does not offer history. |
| Browser text field | Repeat in a browser input and a multiline field; verify the original menu still works. |
| Editor-only companion | Right-click writable input, textarea, contenteditable, read-only/disabled fields, page text, toolbar buttons, and a game/canvas surface. Only recognized writable fields should show the companion, even if another editor owns focus. |
| Explorer and desktop | Paste text, image, and copied files; check filename conflicts and cancellation. |
| Tray and shortcut | Open full history and settings, pause/resume, search, clear, quit; check occupied Ctrl+Shift+V. |
| Custom shortcut | Change keys; confirm old keys are released. Try an occupied combination; confirm the old shortcut still works. Disable and restart. |
| Plain text | Paste formatted Unicode text normally and using the button, Shift+Enter, and Shift+click into an editor and browser. Images/files keep their normal behavior. |
| Pins | Pin/unpin, filter, recopy, restart, fill recent history, reach pin count/size limits, delete, and clear. |
| Search and organization | Combine multiword/typo search with date/type/source/collection filters; rename, recopy, restart, and verify Recent/Favorites order. |
| Transform preview | Inspect and edit each conversion, cancel, and paste into another app; verify the saved original and its formatting survive. |
| Source metadata | Leave recording off, enable for a sample copy, disable, and verify existing source metadata remains. No titles or URLs should be recorded. |
| Update discovery | Open Settings > About, verify the version, request a check, and verify GitHub release discovery or a recoverable offline message. |
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
  It requires writable Edit/Document metadata from UI Automation. Editors without
  suitable metadata are skipped; use the tray or optional shortcut in those apps.
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

## 1.2.1 local validation

On 19 October 2026, Windows 11 x64 build **26300.9711** with .NET SDK **10.0.401**
passed **190 base checks** and **45 interactive checks**. The self-contained
release package passed the file allowlist, version and isolated startup checks.
In **Notepad 11.2607.14.0**, ordinary right-click in a blank editor showed the
companion immediately above the pointer, without closing the original XAML menu.
The displayed companion was 300 x 58 physical pixels at 125% Windows scaling;
its gap was 7 pixels. Notepad's own content zoom was 100%.

The Chrome input/textarea/contenteditable/canvas fixture was prepared, but the
desktop automation tool stopped because it could not establish the browser URL.
Browser interactions therefore remain unverified. No live game was tested;
the negative surface checks use a Windows Forms drawing panel. The scaled
placement tests are geometry checks, not a physical multi-monitor/DPI validation.

## 1.3.0 local validation

On 27 October 2026, Windows 11 x64 build **26300.9711**, .NET SDK **10.0.401**,
and 125% display scaling passed **308 base checks** and **48 interactive checks**.
The interactive executable was launched in its own visible session after the
background-launched attempt could not acquire foreground focus. No assertions
were skipped. It preserved the native menu in three runs, rejected password,
read-only, disabled and game-like controls, and pasted plain, rich and transformed
text into a separate Windows process. UI renderings, including minimum-size
history and transform windows and Settings > About, were visually inspected.
The release build completed with zero warnings/errors and verified the package
file allowlist, EXE version, bundled-runtime startup, and SHA-256 checksums.

The dedicated HTML fixture is `tests/fixtures/editor.html`. An isolated synthetic
history can be prepared with the test executable's `--prepare-manual <empty-dir>`
and opened with `PasteFromHistory.exe --test-data <empty-dir> --background`.
These profiles do not read the normal history database; quit the isolated instance
with the same `--test-data` path and `--quit` when finished.

The browser fixture was prepared, but native browser control stopped because the
automation tool could not establish the active URL. Current Notepad, browser,
Office, real-game, fresh-machine-without-.NET, and physical multi-monitor/DPI
interaction tests were not completed for this release. Prior Notepad validation
and automated native-control tests do not establish coverage of those versions.

## 1.3.1 compatibility validation

On 27 October 2026, the development build passed **312 base checks** and **54
interactive native-window checks**. The interactive suite includes owned Word-style
menus, adjacent toolbar placement, read-only/password/disabled controls, and paste
into a separate process. An initial run lost test-window focus during concurrent
desktop interaction; the uninterrupted rerun passed all 54 checks.

Live checks used an isolated `--test-data` profile with invented clips and
separate test documents. The normal history database was not used. Test mode
exposes transient companion/picker windows on the taskbar for desktop tooling;
the companion retains its no-activate behavior.

| Application | Observed result |
| --- | --- |
| Notepad **11.2607.14.0** | Ordinary right-click kept the native menu open. Selecting the saved Team appointment clip pasted `Blue calendar meeting` at the original caret. |
| Word **16.0.20527.20088** | Ordinary right-click kept the context menu and mini toolbar open. The companion appeared immediately to their left with a 7-pixel gap, covering neither popup. Choosing the validation clip pasted `PasteFromHistory 1.3 validation — ä € 🙂` at the original caret. |
| Word title/tab strip | The UIA Tab surface was rejected and no companion appeared. |
| Chrome **156.0.8078.48** | Not completed: the browser tool's saved origin permission blocked the local fixture. Native right-click and history-paste behavior remains unverified in this Chrome version. |

The Word failure had two independently observed causes. Its writable Edit child
reports `IsKeyboardFocusable=false` because focus belongs to the containing
Document; explicit writable text metadata now determines eligibility. Its context
menu becomes the foreground `Net UI Tool Window`, so comparing only top-level
window identity rejected the otherwise valid request. Recognition is now limited
to same-process menus whose ownership reaches the original document, and placement
accounts for the adjacent mini toolbar.

These results cover the specified Notepad and Word interactions, not all Office
applications, browsers, protected documents, or physical multi-monitor/DPI setups.
