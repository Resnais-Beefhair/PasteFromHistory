param(
    [string]$DotNetPath = 'dotnet',
    [switch]$SkipTests
)
. (Join-Path $PSScriptRoot 'Common.ps1')
Initialize-BuildEnvironment $DotNetPath
$version = Get-AppVersion
$name = "PasteFromHistory-$version-win-x64"
$dist = Join-Path $ProjectRoot 'dist'
# A fresh staging directory prevents old build outputs entering a release.
$package = Join-Path $ProjectRoot ('.build\packages-out\' + [guid]::NewGuid() + '\PasteFromHistory')
$null = New-Item -ItemType Directory -Path $package, $dist -Force
Push-Location $ProjectRoot
try {
    if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') -DotNetPath $DotNet }
    Invoke-DotNet @('publish', 'PasteFromHistory.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--nologo',
        '-warnaserror', "-p:RestoreConfigFile=$ProjectRoot\NuGet.Config", '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishTrimmed=false', '-p:DebugType=None', '-o', $package)
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'docs\USAGE.md') -Destination (Join-Path $package 'README.md')
    foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'Install.cmd', 'Uninstall.cmd')) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot $file) -Destination $package
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $package 'scripts'), (Join-Path $package 'licenses')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1'), (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination (Join-Path $package 'scripts')

    # Read the resolved runtime pack versions, rather than choosing cached versions.
    $assets = Get-Content -LiteralPath (Join-Path $ProjectRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json
    $dependencies = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies)
    foreach ($id in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $dependency = $dependencies | Where-Object { $_.name -eq $id } | Select-Object -First 1
        if (-not $dependency) { throw "Runtime pack was not resolved: $id" }
        $runtimeVersion = ($dependency.version -replace '[\[\] ]', '').Split(',')[0]
        $runtimeRoot = Join-Path $env:NUGET_PACKAGES ($id.ToLowerInvariant() + '\' + $runtimeVersion)
        $legalFiles = if ($id -like 'Microsoft.NETCore.*') { @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') } else { @('LICENSE') }
        foreach ($legalFile in $legalFiles) {
            $legalPath = Join-Path $runtimeRoot $legalFile
            if (-not (Test-Path -LiteralPath $legalPath)) { throw "Runtime license missing: $legalPath" }
            Copy-Item -LiteralPath $legalPath -Destination (Join-Path $package "licenses\$id-$legalFile")
        }
    }
    $exe = Join-Path $dist "$name.exe"
    $zip = Join-Path $dist "$name.zip"
    Copy-Item -LiteralPath (Join-Path $package 'PasteFromHistory.exe') -Destination $exe -Force
    Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
    $hashes = foreach ($file in @($exe, $zip)) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $file)
    }
    $hashes | Set-Content -LiteralPath (Join-Path $dist "$name-SHA256SUMS.txt") -Encoding ascii
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -Path $zip -Version $version
    Write-Output "Ready: $zip"
    Write-Output "Standalone executable: $exe"
}
finally { Pop-Location }
