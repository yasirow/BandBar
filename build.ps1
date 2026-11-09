<#
.SYNOPSIS
    Builds BandBar while keeping every SDK and NuGet side effect inside this folder (docs/DESIGN.md).

.PARAMETER Configuration
    Debug or Release (default).

.PARAMETER Test
    Also build and run the unit tests (tests\BandBar.Tests).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Test
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$project = Join-Path $root 'src\BandBar\BandBar.csproj'
$testProject = Join-Path $root 'tests\BandBar.Tests\BandBar.Tests.csproj'

# SDK and NuGet per-user state goes into the repository instead of the user profile.
# On first use NuGet writes a default user NuGet.Config under %APPDATA% and a migrations marker under the
# LocalAppData known folder, even with --configfile. For this build's processes the profile, and with it both
# folders, point inside the repository.
$userHome = Join-Path $root '.dotnet-home'
$env:USERPROFILE = $userHome
$env:APPDATA = Join-Path $userHome 'AppData\Roaming'
$env:LOCALAPPDATA = Join-Path $userHome 'AppData\Local'
$env:DOTNET_CLI_HOME = $userHome
$env:NUGET_PACKAGES = Join-Path $root '.packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $root '.nuget\plugins-cache'
$env:NUGET_SCRATCH = Join-Path $root '.nuget\scratch'

# Temporary files of the build and of test runs stay inside the repository too.
$tempDir = Join-Path $userHome 'Temp'
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$env:TEMP = $tempDir
$env:TMP = $tempDir

# No telemetry, no first-run side effects, no background update checks.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'

# Leave no MSBuild or compiler server processes running after the build.
$env:MSBUILDDISABLENODEREUSE = '1'

$dotnet = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) { throw '.NET SDK 10 not found. Install it into .dotnet\ (see CONTRIBUTING.md).' }
    $dotnet = $cmd.Source
}

# --force: regenerate obj\*.nuget.g.props every time, since they may hold profile-relative package paths written
# by a build outside this script (an IDE, for example). Packages are already local, so this costs about a second.
function Restore([string] $projectFile) {
    $restoreArgs = @('restore', $projectFile, '--configfile', (Join-Path $root 'nuget.config'), '--force')
    if (Test-Path (Join-Path (Split-Path $projectFile -Parent) 'packages.lock.json')) { $restoreArgs += '--locked-mode' }
    & $dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Restore $project
& $dotnet build $project -c $Configuration --no-restore -nodeReuse:false -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Test) {
    # The test project references the app project; restoring it restores both.
    Restore $testProject
    & $dotnet test $testProject -c $Configuration --no-restore -nodeReuse:false -p:UseSharedCompilation=false `
        --results-directory (Join-Path $userHome 'TestResults')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

exit 0
