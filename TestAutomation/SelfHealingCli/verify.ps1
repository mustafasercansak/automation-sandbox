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

# --compact (#620): same decision and exit code, a smaller result with at most 5 candidates and no subtrees.
Write-Host 'Running the packed tool with --compact...'
$fullOut = & $toolExe --expected (Join-Path $PSScriptRoot 'fixtures/expected-desktop.json') --tree (Join-Path $PSScriptRoot 'fixtures/tree-desktop.json')
$fullExit = $LASTEXITCODE
$compactOut = & $toolExe --expected (Join-Path $PSScriptRoot 'fixtures/expected-desktop.json') --tree (Join-Path $PSScriptRoot 'fixtures/tree-desktop.json') --compact
if ($LASTEXITCODE -ne $fullExit) { throw "Expected --compact to keep exit code $fullExit, got $LASTEXITCODE." }
$full = $fullOut | ConvertFrom-Json
$compact = $compactOut | ConvertFrom-Json
if ($compact.Matched.AutomationId -ne $full.Matched.AutomationId -or $compact.Score -ne $full.Score) { throw '--compact changed the decision.' }
if ($compact.CandidateCount -ne $full.CandidateCount) { throw '--compact must still report the full CandidateCount.' }
if (@($compact.Candidates).Count -gt 5) { throw "Expected at most 5 candidates with --compact, got $(@($compact.Candidates).Count)." }
if (($compactOut | Measure-Object -Character).Characters -ge ($fullOut | Measure-Object -Character).Characters) { throw '--compact output was not smaller.' }

# Selector mode (#616): no snapshot, only the locator string a broken test still carries.
# The partial evidence must be stated in the output and the derived element must carry
# exactly the signals the selector gave. Exit code is not asserted: with this little
# evidence the gate may legitimately refuse (that is the documented behavior).
Write-Host 'Running the packed tool in --selector mode...'
$selectorOut = & $toolExe --selector "getByRole('button', { name: 'Submit' })" --tree (Join-Path $PSScriptRoot 'fixtures/tree.json')
$selectorResult = $selectorOut | ConvertFrom-Json
if (-not $selectorResult.PartialEvidence) { throw 'Expected PartialEvidence=true in --selector output.' }
if ($selectorResult.DerivedExpected.ControlType -ne 'Button' -or $selectorResult.DerivedExpected.Name -ne 'Submit') {
    throw "Expected DerivedExpected Button/Submit, got '$($selectorResult.DerivedExpected.ControlType)'/'$($selectorResult.DerivedExpected.Name)'."
}
if ($selectorResult.Result.Matched.AutomationId -ne 'btn-submit-new') {
    throw "Expected --selector mode to find 'btn-submit-new', got '$($selectorResult.Result.Matched.AutomationId)'."
}

& $toolExe --selector "page.locator('#x')" --tree (Join-Path $PSScriptRoot 'fixtures/tree.json') 2>$null | Out-Null
if ($LASTEXITCODE -ne 2) { throw "Expected exit code 2 for an unsupported selector, got $LASTEXITCODE." }
# The expected non-zero exit above would otherwise become this script's own exit code.
$global:LASTEXITCODE = 0

Write-Host 'Self-Healing CLI verification passed.' -ForegroundColor Green
