# Contributing

Issues and pull requests in English are welcome. For a substantial
feature, describe the intended behavior in an issue before implementing it.
Changes to clipboard capture, native menus, and focus need particular care.

## Set up

Use Windows 11 x64, Git, and the latest stable [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
The SDK includes the desktop targeting tools; the runtime alone cannot build this project.
PowerShell 5.1 or newer is sufficient. No Visual Studio installation is required.

Clone your fork using its GitHub **Code → HTTPS** URL, then open `PasteFromHistory.slnx`
in an editor or run these commands from the repository root:

```powershell
dotnet build PasteFromHistory.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
```

The test executable is a small STA regression harness, not a `dotnet test` adapter.
Use `scripts/Test.ps1` to actually execute the checks. Tests write only synthetic
fixtures and preview images beneath `artifacts/tests`; they do not read the
personal clipboard history or alter Windows integration.

## Run an isolated development instance

```powershell
dotnet run --project PasteFromHistory.csproj -- --test-data "$PWD\artifacts\dev-profile"
```

`--test-data` changes the history/settings directory and single-instance scope,
and skips Explorer and automatic-start registry changes. It still observes the
live clipboard and mouse. Use synthetic content and close your normal instance
when testing the global shortcut to avoid a shortcut conflict. Never commit the
test profile, even if you think it contains only sample data.

The fixture window for manual paste tests is:

```powershell
.\tests\bin\Release\net10.0-windows\PasteFromHistory.Tests.exe --ui
```

For changes involving right-click menus or focus, run the additional checks from
an unlocked, interactive desktop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -Interactive
```

The menu test briefly activates its own text window. It cannot validate another
application's custom menu, so also follow the [manual test matrix](docs/TESTING.md).

## Submit a change

- Use a short topic branch and keep the PR focused on one problem.
- Follow `.editorconfig` and the existing C# style. Explain native API flags and
  focus behavior when their purpose is not obvious.
- Add regression coverage for behavior changes. Do not suppress a failing test
  to make CI green.
- Update user documentation and `CHANGELOG.md` when behavior changes.
- Run `scripts/Build.ps1` before proposing a release-related change. It builds,
  tests, packages, and smoke-tests the self-contained executable.
- Include reproduction steps and actual validation results in the PR.
- Use invented clipboard text and images in screenshots and reports. Never
  attach `history.dat`, `settings.json`, personal files, or private paths.

Contributions are accepted under the project's [MIT license](LICENSE). Treat
other contributors respectfully; discuss the code and behavior, not the person.
See [SECURITY.md](SECURITY.md) for private vulnerability reports.
