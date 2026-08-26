[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'PlaywrightEndToEndQuickstart.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    Write-Host 'Building and running Playwright End-to-End Quickstart sample...'
    Invoke-DotNet @('run', '--project', $projectPath)
    Write-Host 'Playwright End-to-End Quickstart verification passed.' -ForegroundColor Green
}
finally {
    # Clean up any generated artifacts from the run
    $reportJson = Join-Path $PSScriptRoot 'healing-report.json'
    $reportHtml = Join-Path $PSScriptRoot 'healing-report.html'
    $locatorsJson = Join-Path $PSScriptRoot 'locators.json'
    $locatorsLock = Join-Path $PSScriptRoot 'locators.json.lock'

    if (Test-Path -LiteralPath $reportJson) { Remove-Item -LiteralPath $reportJson -Force }
    if (Test-Path -LiteralPath $reportHtml) { Remove-Item -LiteralPath $reportHtml -Force }
    if (Test-Path -LiteralPath $locatorsJson) { Remove-Item -LiteralPath $locatorsJson -Force }
    if (Test-Path -LiteralPath $locatorsLock) { Remove-Item -LiteralPath $locatorsLock -Force }
}
