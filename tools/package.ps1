<#
.SYNOPSIS
    Builds BandBar and creates the release packages in artifacts\ (docs/DESIGN.md §10).

.DESCRIPTION
    - BandBar-<version>-portable.zip  the executable and its notices
    - BandBar-Setup-<version>.exe     per-user installer (needs Inno Setup 6)
    - SHA256SUMS.txt                    checksums of both

.PARAMETER RequireInstaller
    Fail when Inno Setup is not available (used by CI). Without it, the installer is skipped with a warning.

.PARAMETER SkipBuild
    Package the existing Release build instead of building (and testing) first.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\package.ps1
#>
[CmdletBinding()]
param(
    [switch] $RequireInstaller,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\BandBar\BandBar.csproj'
$binDir = Join-Path $root 'src\BandBar\bin\Release\net48'
$artifacts = Join-Path $root 'artifacts'

[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode) { throw 'No <Version> in the project file.' }
$version = $versionNode.InnerText.Trim()
Write-Host "BandBar $version"

if (-not $SkipBuild) {
    # A separate process, so the build's environment redirection does not leak into packaging.
    # -Test: no package is made unless the unit tests pass.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -Configuration Release -Test
    if ($LASTEXITCODE -ne 0) { throw "Build or tests failed ($LASTEXITCODE)." }
}

foreach ($file in 'BandBar.exe', 'BandBar.exe.config') {
    if (-not (Test-Path -LiteralPath (Join-Path $binDir $file))) { throw "Missing build output: $file" }
}

if (Test-Path -LiteralPath $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

# Portable ZIP: explicit files only, never settings or logs from a test run in bin\.
$staging = Join-Path $artifacts "BandBar-$version"
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath (Join-Path $binDir 'BandBar.exe') -Destination $staging
Copy-Item -LiteralPath (Join-Path $binDir 'BandBar.exe.config') -Destination $staging
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $staging 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $staging 'THIRD-PARTY-NOTICES.txt')
$zip = Join-Path $artifacts "BandBar-$version-portable.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -LiteralPath $staging -Recurse -Force
Write-Host "Created $(Split-Path $zip -Leaf)"

# Installer
$iscc = @(
    $env:ISCC,
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$binDir" "/DOutputDir=$artifacts" (Join-Path $root 'installer\BandBar.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
    Write-Host "Created BandBar-Setup-$version.exe"
}
elseif ($RequireInstaller) {
    throw 'Inno Setup 6 (ISCC.exe) not found.'
}
else {
    Write-Warning 'Inno Setup 6 not found; installer skipped (the portable ZIP was created).'
}

# Checksums in the common "<hash>  <file>" format.
$lines = Get-ChildItem -LiteralPath $artifacts -File |
    Where-Object { $_.Extension -in '.zip', '.exe' } |
    Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllLines((Join-Path $artifacts 'SHA256SUMS.txt'), [string[]] $lines, (New-Object Text.UTF8Encoding($false)))
Write-Host 'Created SHA256SUMS.txt'
$lines | ForEach-Object { Write-Host "  $_" }
