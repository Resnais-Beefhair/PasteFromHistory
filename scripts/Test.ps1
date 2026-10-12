param(
    [string]$DotNetPath = 'dotnet',
    [switch]$Interactive
)
. (Join-Path $PSScriptRoot 'Common.ps1')
Initialize-BuildEnvironment $DotNetPath
Push-Location $ProjectRoot
try {
    Invoke-DotNet @('build', 'tests\PasteFromHistory.Tests.csproj', '-c', 'Release', '--nologo', '-warnaserror', "-p:RestoreConfigFile=$ProjectRoot\NuGet.Config")
    $testExe = Join-Path $ProjectRoot 'tests\bin\Release\net10.0-windows\PasteFromHistory.Tests.exe'
    $testOutput = Join-Path $ProjectRoot 'artifacts\tests'
    & $testExe $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
    if ($Interactive) {
        & $testExe $testOutput --menu-regression
        if ($LASTEXITCODE -ne 0) { throw 'Interactive menu regression tests failed. Use an unlocked desktop session.' }
    }
}
finally { Pop-Location }
