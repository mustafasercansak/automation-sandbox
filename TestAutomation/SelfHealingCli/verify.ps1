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

# Fixture: a stale "expected" element (AutomationId renamed) against a live tree that
# otherwise matches exactly on Name/ControlType/ParentControlType/position - the same
# scenario TestAutomation/NUnitFixtureTests proves the library heals confidently, reused
# here to exercise the packed tool end to end, not just the underlying library call.
$expectedPath = Join-Path $PSScriptRoot 'fixtures/expected.json'
$treePath = Join-Path $PSScriptRoot 'fixtures/tree.json'

Write-Host 'Running the packed tool against the fixture...'
$stdout = & $toolExe --expected $expectedPath --tree $treePath --profile balanced
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "Expected the packed tool to confidently heal the fixture locator (exit 0), got exit code $exitCode."
}

$result = $stdout | ConvertFrom-Json
if (-not $result.IsConfident) {
    throw 'Expected IsConfident=true in the tool output.'
}

if ($result.Matched.AutomationId -ne 'btn-submit-new') {
    throw "Expected Matched.AutomationId 'btn-submit-new', got '$($result.Matched.AutomationId)'."
}

Write-Host 'Self-Healing CLI verification passed.' -ForegroundColor Green
