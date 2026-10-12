param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Version
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$allowed = @(
    'PasteFromHistory/PasteFromHistory.exe', 'PasteFromHistory/README.md', 'PasteFromHistory/LICENSE',
    'PasteFromHistory/THIRD-PARTY-NOTICES.md', 'PasteFromHistory/Install.cmd', 'PasteFromHistory/Uninstall.cmd',
    'PasteFromHistory/scripts/Install.ps1', 'PasteFromHistory/scripts/Uninstall.ps1',
    'PasteFromHistory/licenses/Microsoft.NETCore.App.Runtime.win-x64-LICENSE.TXT',
    'PasteFromHistory/licenses/Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.TXT',
    'PasteFromHistory/licenses/Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE'
)
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name -ne '' } | ForEach-Object { $_.FullName.Replace('\', '/') })
    if (@(Compare-Object $allowed $entries).Count -ne 0) { throw 'Package contents do not match the release allowlist.' }
}
finally { $archive.Dispose() }
$projectRoot = Split-Path -Parent $PSScriptRoot
$temp = Join-Path $projectRoot ('artifacts\package-check\' + [guid]::NewGuid())
Expand-Archive -LiteralPath $Path -DestinationPath $temp
$exe = Join-Path $temp 'PasteFromHistory\PasteFromHistory.exe'
if ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.Split('+')[0] -ne $Version) { throw 'EXE version differs from package version.' }
# --quit with a unique data path initializes Windows Forms and the bundled runtime,
# but cannot contact the user's running instance or change Windows integration.
$testData = Join-Path $temp 'isolated-data'
$client = Start-Process -FilePath $exe -ArgumentList @('--test-data', ('"' + $testData + '"'), '--quit') -WindowStyle Hidden -PassThru
if (-not $client.WaitForExit(30000)) { throw 'Packaged executable did not exit within 30 seconds.' }
if ($client.ExitCode -ne 0) { throw "Packaged executable failed (exit $($client.ExitCode))." }
Write-Output 'Package verified: file allowlist, version, and isolated executable smoke test.'
