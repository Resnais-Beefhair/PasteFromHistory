Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Initialize-BuildEnvironment([string]$DotNetPath) {
    $script:ProjectRoot = Split-Path -Parent $PSScriptRoot
    $script:DotNet = (Get-Command $DotNetPath -ErrorAction Stop).Source
    $env:DOTNET_ROOT = Split-Path -Parent $script:DotNet
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:DOTNET_CLI_HOME = Join-Path $script:ProjectRoot '.build\cli'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:NUGET_PACKAGES = Join-Path $script:ProjectRoot '.build\packages'
}

function Invoke-DotNet([string[]]$Arguments) {
    & $script:DotNet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed (exit $LASTEXITCODE)." }
}

function Get-AppVersion {
    [xml]$versionFile = Get-Content -LiteralPath (Join-Path $script:ProjectRoot 'Version.props') -Raw
    $version = [string]$versionFile.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version.props must contain a stable major.minor.patch version.' }
    return $version
}
