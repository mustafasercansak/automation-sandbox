[CmdletBinding()]
param(
    # The exact version to verify. Passed by release.yml as the just-published
    # PACKAGE_VERSION. When omitted, falls back to Directory.Build.props <Version>
    # (useful for a manual check once a version is already on nuget.org).
    [string] $Version
)

# Post-publish consumer check (#507), mirroring
# samples/HeuristicHealingQuickstart/verify-published.ps1's pattern: installs the EXACT
# published version from nuget.org, proving the real artifact resolves and runs - not
# just that packing succeeded. "dotnet tool install" has no restore-only-from-this-source
# flag the way "dotnet restore --source" does, so this relies on the runner's default
# nuget.config only listing nuget.org (true for a clean GitHub Actions runner); it is not
# as strictly isolated as the library verification's --packages/--source combination.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

if (-not $Version) {
    $propsPath = Join-Path $repoRoot 'Directory.Build.props'
    $propsText = Get-Content -LiteralPath $propsPath -Raw
    if ($propsText -match '<Version>(?<version>[^<]+)</Version>') {
        $Version = $Matches['version'].Trim()
    } else {
        throw 'Could not extract <Version> from Directory.Build.props and no -Version was supplied.'
    }
}
Write-Host "Verifying published AutomationSandbox.Cli version: $Version"

$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('automation-sandbox-cli-consumer-' + [Guid]::NewGuid().ToString('N'))
$toolInstallDir = Join-Path $verificationRoot 'tool'

try {
    New-Item -ItemType Directory -Path $toolInstallDir | Out-Null

    # nuget.org indexing lags publish by more than a few minutes for a low-traffic
    # package (#367 was exactly this for the libraries) - poll instead of assuming the
    # package is installable the instant the push command returns.
    $maxAttempts = if ($env:VERIFY_PUBLISHED_MAX_ATTEMPTS) { [int]$env:VERIFY_PUBLISHED_MAX_ATTEMPTS } else { 30 }
    $delaySeconds = if ($env:VERIFY_PUBLISHED_DELAY_SECONDS) { [int]$env:VERIFY_PUBLISHED_DELAY_SECONDS } else { 50 }
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        Write-Host "Install attempt $attempt/$maxAttempts (nuget.org, no cache)..."
        & dotnet tool install --tool-path $toolInstallDir --version $Version --add-source https://api.nuget.org/v3/index.json --no-cache AutomationSandbox.Cli
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq $maxAttempts) {
            throw "AutomationSandbox.Cli version $Version was not installable from nuget.org after $maxAttempts attempts (~$([math]::Round($maxAttempts * $delaySeconds / 60)) min). The package may still be indexing - re-run this workflow; the nuget.org push step skips what already exists."
        }
        Write-Host "Not indexed yet; waiting $delaySeconds s..."
        Start-Sleep -Seconds $delaySeconds
    }

    $toolExe = Join-Path $toolInstallDir 'automation-sandbox-heal'
    if ($IsWindows) { $toolExe += '.exe' }
    $fixtureDir = Join-Path $PSScriptRoot 'fixtures'

    function Invoke-FixtureHeal {
        param(
            [Parameter(Mandatory)][string] $Description,
            [Parameter(Mandatory)][string] $ExpectedPath,
            [Parameter(Mandatory)][string] $TreePath,
            [Parameter(Mandatory)][string] $ExpectedHealedAutomationId
        )

        Write-Host "Running the installed tool against the $Description fixture..."
        $stdout = & $toolExe --expected $ExpectedPath --tree $TreePath --profile balanced
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "Expected the published tool to confidently heal the $Description fixture locator (exit 0), got exit code $exitCode."
        }

        $result = $stdout | ConvertFrom-Json
        if (-not $result.IsConfident -or $result.Matched.AutomationId -ne $ExpectedHealedAutomationId) {
            throw "Published tool output did not match the expected $Description fixture result."
        }
    }

    # Both fixtures (#516): web-flavored and the repo's own established WinForms
    # "flagship demo" scenario, proving the published package heals desktop-shaped
    # trees identically to web ones, not just the web case verify-published.ps1
    # originally checked alone.
    Invoke-FixtureHeal -Description 'web' `
        -ExpectedPath (Join-Path $fixtureDir 'expected.json') `
        -TreePath (Join-Path $fixtureDir 'tree.json') `
        -ExpectedHealedAutomationId 'btn-submit-new'

    Invoke-FixtureHeal -Description 'desktop (WinForms)' `
        -ExpectedPath (Join-Path $fixtureDir 'expected-desktop.json') `
        -TreePath (Join-Path $fixtureDir 'tree-desktop.json') `
        -ExpectedHealedAutomationId 'textBox1'

    Write-Host "Published-package consumer verification passed for AutomationSandbox.Cli $Version." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}
