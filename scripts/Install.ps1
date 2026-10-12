$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $PSScriptRoot
$source = $packageRoot
$sourceExe = Join-Path $source 'PasteFromHistory.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Extract the release ZIP and run Install.cmd from the extracted PasteFromHistory folder.' }
$installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PasteFromHistory'
$destinationExe = Join-Path $installRoot 'PasteFromHistory.exe'
$requiredFiles = @('PasteFromHistory.exe', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Uninstall.cmd',
    'scripts\Uninstall.ps1',
    'licenses\Microsoft.NETCore.App.Runtime.win-x64-LICENSE.TXT',
    'licenses\Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.TXT',
    'licenses\Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE')
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $file))) { throw "Missing package file: $file" }
}
# Ask the existing app to flush its own history and exit; never force-kill it.
$client = Start-Process -FilePath $sourceExe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
if (-not $client.WaitForExit(5000)) { throw 'The existing app did not respond. Close it from its tray icon first.' }
if ($client.ExitCode -ne 0) { throw 'The existing app could not be closed.' }
$running = Get-Process -Name PasteFromHistory -ErrorAction SilentlyContinue
foreach ($process in $running) {
    if (-not $process.WaitForExit(5000)) { throw 'A copy operation may still be running. Close PasteFromHistory and try again.' }
}
$null = New-Item -ItemType Directory -Path $installRoot -Force
if ([IO.Path]::GetFullPath($source).TrimEnd('\') -ne [IO.Path]::GetFullPath($installRoot).TrimEnd('\')) {
    foreach ($file in $requiredFiles) {
        $destination = Join-Path $installRoot $file
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination -Force
    }
}
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'PasteFromHistory.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $destinationExe
$shortcut.WorkingDirectory = $installRoot
$shortcut.Description = 'PasteFromHistory'
$shortcut.Save()
Start-Process -FilePath $destinationExe -WindowStyle Normal
Write-Output "Installed: $destinationExe"
Write-Output 'The tray icon opens the history. Right-click it for settings and exit.'
