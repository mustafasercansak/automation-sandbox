[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'CalibrationCli.csproj'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixturePath = Join-Path $repoRoot 'TestAutomation/ScenarioRunner/Fixtures/HandBrake_1.8.2.tree.json'
$reportPath = Join-Path $PWD.Path 'HandBrake-calibration-report.md'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    Write-Host 'Building and running the calibration CLI against the frozen HandBrake fixture...'
    Invoke-DotNet @('run', '--project', $projectPath, '--', $fixturePath, '--app', 'HandBrake')

    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "Expected report was not written to $reportPath."
    }

    $reportText = Get-Content -LiteralPath $reportPath -Raw
    if ($reportText -notmatch 'Recommended Profile') {
        throw 'Report did not contain a profile recommendation.'
    }

    Write-Host 'Calibration CLI verification passed.' -ForegroundColor Green
}
finally {
    # Clean up the generated report. Program.cs writes it relative to
    # Directory.GetCurrentDirectory(), which is this script's own invocation
    # directory ($PWD), not $PSScriptRoot.
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath -Force }
}
