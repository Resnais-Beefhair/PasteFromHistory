$ErrorActionPreference = 'Stop'
$localData = [Environment]::GetFolderPath('LocalApplicationData')
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $localData 'Programs\PasteFromHistory'))
$exe = Join-Path $expectedRoot 'PasteFromHistory.exe'
if (Test-Path -LiteralPath $exe) {
    $client = Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden -PassThru
    if (-not $client.WaitForExit(5000) -or $client.ExitCode -ne 0) { throw 'Close PasteFromHistory from its tray icon first.' }
    foreach ($process in (Get-Process -Name PasteFromHistory -ErrorAction SilentlyContinue)) {
        if (-not $process.WaitForExit(5000)) { throw 'A copy operation is still running. Try again after it completes.' }
    }
    $remove = Start-Process -FilePath $exe -ArgumentList '--remove-integration' -WindowStyle Hidden -PassThru
    if (-not $remove.WaitForExit(5000) -or $remove.ExitCode -ne 0) { throw 'Removing Windows integration failed.' }
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'PasteFromHistory.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
if (Test-Path -LiteralPath $expectedRoot) {
    $resolvedRoot = (Resolve-Path -LiteralPath $expectedRoot).ProviderPath
    if (-not $resolvedRoot.Equals($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected installation path; aborting removal.' }
    if ((Get-Item -LiteralPath $resolvedRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installation path is a link; aborting removal.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
Write-Output 'PasteFromHistory removed. Your encrypted history and settings were preserved in:'
Write-Output (Join-Path $localData 'Leikehistoria')
