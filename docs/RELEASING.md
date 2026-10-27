# Publishing and releases

The repository contains source, documentation, and a small synthetic UI preview.
EXEs, ZIPs, local SDKs, logs, settings, and history databases are excluded from
Git. Distribute compiled downloads as **GitHub Release assets**.

## First GitHub publication

1. Review the tracked file list with `git ls-files`. It must not contain `.build/`,
   `artifacts/`, `dist/`, `bin/`, `obj/`, certificates, settings, or clipboard history.
2. Create an empty GitHub repository. Do not initialize a second README/license.
   Set the visibility deliberately, then use the repository's provided remote URL:

   ```powershell
   git remote add origin <repository-HTTPS-URL>
   git push -u origin main
   ```

3. Enable Actions and **private vulnerability reporting** in repository settings.
   Require the `Build and verify` CI check on `main` after its first successful run.
   The default workflow token should remain read-only; the release job explicitly
   requests contents-write permission.
4. Add a concise repository description and topics such as `windows`, `clipboard`,
   `winforms`, and `dotnet`. Confirm README links and the preview render correctly.

These are maintainer steps. Preparing the repository locally does not publish it.

## Build a release

1. Set the version in `Version.props` and update `CHANGELOG.md` with the release
   date. Use `major.minor.patch`; tags must be exactly `v` plus that version.
2. Install the latest stable .NET 10 SDK. `global.json` accepts newer .NET 10
   feature bands, and CI uses `10.0.x`. Self-contained releases must be rebuilt
   for runtime security updates; changing the user's system .NET is insufficient.
3. Run the full build and the interactive menu, shortcut, capture, and paste checks:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -Interactive
   ```

4. Complete the [manual test matrix](TESTING.md), especially Notepad, installation,
   upgrade, and a machine without a preinstalled .NET runtime. State any coverage
   gaps in the release notes. Do not describe unperformed tests as passing.
5. Check the ZIP contents and `-SHA256SUMS.txt`. Keep the app's MIT license and
   bundled runtime notices in the download. Runtime pack versions are in
   `obj/project.assets.json` and the build log.
6. Commit the change, wait for CI, and push an annotated tag:

   ```powershell
   git tag -a v1.3.0 -m "PasteFromHistory 1.3.0"
   git push origin v1.3.0
   ```

The tag workflow validates the tag against `Version.props`, builds from the tagged
commit, and creates a **draft** GitHub Release with the EXE, ZIP, checksums, and
license notice assets. Review the draft, replace the validation placeholder with
actual results, and publish it manually. It is never automatically published.

For the first release, or when a local Git push is unavailable, open
**Actions → Draft release → Run workflow** on `main`. This builds that exact
commit and prepares the version tag and draft from `Version.props`. Review and
publish the draft after the workflow succeeds. Do not reuse an existing version.
The workflow creates a missing tag before drafting and checks that it resolves
to the exact workflow source commit. An existing tag pointing elsewhere fails
the release instead of being moved.

After publication, download the EXE, ZIP, and checksums from that public tag.
Verify both hashes and the ZIP contents before installing. Confirm that the
running process, per-user installed EXE, Start menu shortcut, and any enabled
startup/Explorer commands use the updated installation. A published release
alone does not update a running local copy. Settings > About checks GitHub only
when requested; it does not perform installation.

## Downloads

- `PasteFromHistory-<version>-win-x64.zip`: recommended, includes the app, optional
  installer/uninstaller, user guide, and license notices.
- `PasteFromHistory-<version>-win-x64.exe`: standalone application; no runtime install.
  Keep it at a stable path after first launch because shell commands use that path.
- `PasteFromHistory-<version>-win-x64-SHA256SUMS.txt`: hashes of the EXE and ZIP.
- `LICENSE` and `THIRD-PARTY-NOTICES.md`: license information beside the download;
  full runtime notices are inside the ZIP.

Verify a download with `Get-FileHash .\<downloaded-file> -Algorithm SHA256` and
compare it with the checksum file from the same release. Checksums are not a
code-signing identity. Builds are currently unsigned; do not tell users to
disable antivirus or SmartScreen globally.

If code signing is introduced, keep the certificate outside Git, sign the EXE
**before** packaging and checksum generation, and verify the signature in CI.
Do not re-upload changed binaries under an existing published version; make a
new release so hashes and source provenance stay understandable.

## Build customization

`Build.ps1` and `Test.ps1` accept `-DotNetPath <path-to-dotnet.exe>` for a local
SDK. `Build.ps1 -SkipTests` is for local iteration; release CI always runs tests.
The bundle is intentionally untrimmed because Windows Forms does not support
trimming this application safely. Native runtime libraries are extracted by .NET
under the current user's temporary directory at launch.

Only x64 is packaged and verified. Adding ARM64 requires extending the packaging
allowlist, installer notices, CI, and manual validation rather than renaming a ZIP.
