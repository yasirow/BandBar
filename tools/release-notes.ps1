<#
.SYNOPSIS
    Writes the GitHub release notes for one version: its CHANGELOG.md section plus download and verification help.

.DESCRIPTION
    Versions that were never published on their own (their link at the bottom of CHANGELOG.md does not point to a
    GitHub release) are part of the next release: their sections follow under "Also in this release", down to the
    previous published version.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\release-notes.ps1 -Version 0.1.3 -OutFile artifacts\RELEASE_NOTES.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
$changelog = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding UTF8

# Sections in file order; the link references at the end ("[0.7.0]: https://...") close the last one.
$sections = New-Object System.Collections.Generic.List[object]
$released = @{}
$current = $null
foreach ($line in $changelog) {
    if ($line -match '^## \[(?<v>[^\]]+)\](?<rest>.*)$') {
        $current = [pscustomobject]@{ Version = $Matches.v; Rest = $Matches.rest.Trim(); Lines = New-Object System.Collections.Generic.List[string] }
        $sections.Add($current)
        continue
    }
    if ($line -match '^\[(?<v>[^\]]+)\]:\s*(?<url>\S+)') {
        $current = $null
        $linkVersion = $Matches.v # read before the next -match replaces $Matches
        $released[$linkVersion] = $Matches.url -match '/releases/tag/'
        continue
    }
    if ($current) { $current.Lines.Add($line) }
}

$index = -1
for ($i = 0; $i -lt $sections.Count; $i++) { if ($sections[$i].Version -eq $Version) { $index = $i; break } }
if ($index -lt 0) { throw "CHANGELOG.md has no section for $Version." }
$body = ($sections[$index].Lines -join "`n").Trim()

# Older versions without a release of their own, newest first, down to the previous published version.
$earlier = New-Object System.Collections.Generic.List[string]
for ($i = $index + 1; $i -lt $sections.Count; $i++) {
    $s = $sections[$i]
    if (-not $released.ContainsKey($s.Version) -or $released[$s.Version]) { break }
    $earlier.Add("### $($s.Version) $($s.Rest)".TrimEnd())
    $earlier.Add('')
    $earlier.Add((($s.Lines | ForEach-Object { $_ -replace '^### ', '#### ' }) -join "`n").Trim())
    $earlier.Add('')
}
if ($earlier.Count -gt 0) {
    $body += "`n`n## Also in this release`n`nThese versions were not published on their own; their changes are part of this release.`n`n" + ($earlier -join "`n").Trim()
}

$notes = @"
$body

## Download

| File | For |
|---|---|
| **BandBar-Setup-$Version.exe** | Recommended. Installs for the current user, no administrator rights. Installing over an older version upgrades it and keeps your settings. |
| BandBar-$Version-portable.zip | Unzip anywhere and run ``BandBar.exe``. Delete the folder to remove it. |
| SHA256SUMS.txt | Checksums of both files. |

**Requirements:** Windows 11 or Windows 10 1903+ (64-bit; 1809 with .NET Framework 4.8); a licensed NetLimiter 5 installation.

The files are not code-signed, so Windows SmartScreen may show a warning on first run (**More info → Run anyway**).
To verify a download, compare ``Get-FileHash <file> -Algorithm SHA256`` with ``SHA256SUMS.txt``, or check its build
provenance with ``gh attestation verify <file> --repo yasirow/BandBar``.

BandBar is an independent project and is not affiliated with Locktime Software, the developer of NetLimiter.
"@

$dir = Split-Path $OutFile -Parent
if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[IO.File]::WriteAllText($OutFile, $notes.Replace("`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
Write-Host "Release notes for $Version written to $OutFile"
