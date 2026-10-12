# Third-party components

PasteFromHistory's source and original icon are licensed under the [MIT license](LICENSE).
The application has no third-party NuGet application dependencies.

The self-contained Windows release includes Microsoft's .NET runtime and Windows
Desktop runtime. Their upstream source repositories are
[dotnet/runtime](https://github.com/dotnet/runtime) and
[dotnet/windowsdesktop](https://github.com/dotnet/windowsdesktop), including
[Windows Forms](https://github.com/dotnet/winforms).

These components and their dependencies retain their own licenses. Each release
ZIP includes the unmodified `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` files from
the .NET runtime pack and `LICENSE` from the Windows Desktop runtime pack in its
`licenses/` directory. Use that ZIP
when redistributing the application so the required notices travel with it.

Build tooling, including the .NET SDK and GitHub Actions, is not part of the
application download. Runtime versions are recorded in the release build log;
maintainers must rebuild and release updates when the bundled runtime needs
security fixes. The application has no automatic update service.
