[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'SelfHealingCli.csproj'
$packOutputDir = Join-Path $PSScriptRoot 'bin/pack-verify'
$toolInstallDir = Join-Path $PSScriptRoot 'bin/tool-verify'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $packOutputDir) { Remove-Item -LiteralPath $packOutputDir -Recurse -Force }
if (Test-Path -LiteralPath $toolInstallDir) { Remove-Item -LiteralPath $toolInstallDir -Recurse -Force }

Write-Host 'Packing the CLI as a dotnet tool...'
Invoke-DotNet @('pack', $projectPath, '--configuration', 'Release', '--output', $packOutputDir)

# Directory.Build.props's <Version> is a prerelease identifier (e.g. 0.2.0-beta.7) during
# the beta series. "dotnet tool install" only resolves stable versions unless told
# otherwise, so without --prerelease this fails with a confusing "not found in feed"
# error even though the package is sitting right there in $packOutputDir.
Write-Host 'Installing the packed tool into an isolated local tool directory...'
Invoke-DotNet @('tool', 'install', '--tool-path', $toolInstallDir, '--add-source', $packOutputDir, '--prerelease', 'AutomationSandbox.Cli')

$toolExe = Join-Path $toolInstallDir 'automation-sandbox-heal'
if ($IsWindows) { $toolExe += '.exe' }

function Invoke-FixtureHeal {
    param(
        [Parameter(Mandatory)][string] $Description,
        [Parameter(Mandatory)][string] $ExpectedPath,
        [Parameter(Mandatory)][string] $TreePath,
        [Parameter(Mandatory)][string] $ExpectedHealedAutomationId
    )

    Write-Host "Running the packed tool against the $Description fixture..."
    $stdout = & $toolExe --expected $ExpectedPath --tree $TreePath --profile balanced
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "Expected the packed tool to confidently heal the $Description fixture locator (exit 0), got exit code $exitCode."
    }

    $result = $stdout | ConvertFrom-Json
    if (-not $result.IsConfident) {
        throw "Expected IsConfident=true for the $Description fixture."
    }

    if ($result.Matched.AutomationId -ne $ExpectedHealedAutomationId) {
        throw "Expected Matched.AutomationId '$ExpectedHealedAutomationId' for the $Description fixture, got '$($result.Matched.AutomationId)'."
    }
}

# Web-flavored fixture: a stale "expected" element (AutomationId renamed) against a live
# tree that otherwise matches exactly on Name/ControlType/ParentControlType/position - the
# same scenario TestAutomation/NUnitFixtureTests proves the library heals confidently,
# reused here to exercise the packed tool end to end, not just the underlying library call.
Invoke-FixtureHeal -Description 'web' `
    -ExpectedPath (Join-Path $PSScriptRoot 'fixtures/expected.json') `
    -TreePath (Join-Path $PSScriptRoot 'fixtures/tree.json') `
    -ExpectedHealedAutomationId 'btn-submit-new'

# Desktop-flavored fixture (#516): the repo's own established WinForms "flagship demo"
# scenario (SelfHealingResolverTests.Resolve_WinFormsDemoScenario_KeepsMarginAboveThreshold
# / BuildCurrentMainFormTree) - txtEmail renamed to textBox1 against its real ~0.057
# runner-up margin (sibling txtLastName) - proving the CLI is not web-only: the resolver
# underneath draws no platform distinction, and this is the same scenario already proven
# elsewhere in the test suite, not a newly invented one.
Invoke-FixtureHeal -Description 'desktop (WinForms)' `
    -ExpectedPath (Join-Path $PSScriptRoot 'fixtures/expected-desktop.json') `
    -TreePath (Join-Path $PSScriptRoot 'fixtures/tree-desktop.json') `
    -ExpectedHealedAutomationId 'textBox1'

Write-Host 'Self-Healing CLI verification passed.' -ForegroundColor Green
