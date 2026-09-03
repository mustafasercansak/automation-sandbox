[CmdletBinding()]
param(
    # The exact version to verify. Passed by release.yml as the just-published
    # PACKAGE_VERSION. When omitted, falls back to Directory.Build.props <Version>
    # (useful for a manual check once a version is already on nuget.org).
    [string] $Version
)

# Post-publish consumer check (#336).
#
# Restores the sample EXACTLY as an external consumer would: a clean package
# directory, nuget.org as the only source, no cache. This is the check that the
# real published artifact works end to end. It runs in release.yml after the
# packages are pushed, so it is never blocked on a not-yet-published version the
# way the old per-PR nuget-consumer job was.

$ErrorActionPreference = 'Stop'
$sourceProjectPath = Join-Path $PSScriptRoot 'HeuristicHealingQuickstart.csproj'
$sourceProgramPath = Join-Path $PSScriptRoot 'Program.cs'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$projectText = Get-Content -LiteralPath $sourceProjectPath -Raw
if ($projectText -match '<ProjectReference') {
    throw 'Consumer verification must use published PackageReference entries, not repository ProjectReference entries.'
}

if (-not $Version) {
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    $propsText = Get-Content -LiteralPath $propsPath -Raw
    if ($propsText -match '<Version>(?<version>[^<]+)</Version>') {
        $Version = $Matches['version'].Trim()
    } else {
        throw 'Could not extract <Version> from Directory.Build.props and no -Version was supplied.'
    }
}
Write-Host "Verifying published package version: $Version"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('automation-sandbox-consumer-' + [Guid]::NewGuid().ToString('N'))
$consumerPath = Join-Path $verificationRoot 'consumer'
$packagesPath = Join-Path $verificationRoot 'packages'
$projectPath = Join-Path $consumerPath 'HeuristicHealingQuickstart.csproj'

try {
    New-Item -ItemType Directory -Path $consumerPath | Out-Null
    Copy-Item -LiteralPath $sourceProgramPath -Destination (Join-Path $consumerPath 'Program.cs')

    # Force every AutomationSandbox.* pin to the version under test, so this verifies
    # the release even before the SSoT-bump PR lands.
    $pinned = [regex]::Replace(
        $projectText,
        '(<PackageReference\s+Include="AutomationSandbox\.[^"]+"\s+Version=")[^"]+(")',
        ('${1}' + $Version + '${2}'))
    Set-Content -LiteralPath $projectPath -Value $pinned -NoNewline

    # nuget.org indexing lags publish, and not by "a few minutes": the restore
    # (flat-container) feed for a low-traffic package family routinely takes 10-20+
    # minutes, and the seven packages here index independently, so the slowest one
    # sets the wait. The v0.2.0-beta.5 run failed the old 8x30s = 4-minute budget
    # while the packages were already live in the registration API (#367). Poll up to
    # ~25 minutes; override with VERIFY_PUBLISHED_MAX_ATTEMPTS / _DELAY_SECONDS.
    $maxAttempts = if ($env:VERIFY_PUBLISHED_MAX_ATTEMPTS) { [int]$env:VERIFY_PUBLISHED_MAX_ATTEMPTS } else { 30 }
    $delaySeconds = if ($env:VERIFY_PUBLISHED_DELAY_SECONDS) { [int]$env:VERIFY_PUBLISHED_DELAY_SECONDS } else { 50 }
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        Write-Host "Restore attempt $attempt/$maxAttempts (nuget.org only, no cache)..."
        & dotnet restore $projectPath --force --no-cache --packages $packagesPath --source https://api.nuget.org/v3/index.json
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq $maxAttempts) {
            throw "Package version $Version was not restorable from nuget.org after $maxAttempts attempts (~$([math]::Round($maxAttempts * $delaySeconds / 60)) min). The packages may still be indexing - re-run this workflow, it is idempotent (release-create and nuget push both skip what already exists)."
        }
        Write-Host "Not indexed yet; waiting $delaySeconds s..."
        Start-Sleep -Seconds $delaySeconds
    }

    Write-Host 'Building the consumer sample...'
    Invoke-DotNet @('build', $projectPath, '--configuration', 'Release', '--no-restore')

    Write-Host 'Running the end-to-end healing scenario...'
    Invoke-DotNet @('run', '--project', $projectPath, '--configuration', 'Release', '--no-build')

    Write-Host "Published-package consumer verification passed for $Version." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}
