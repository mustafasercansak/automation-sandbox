[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'WebObservationQuickstart.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Building and running Web Observation Quickstart sample...'
Invoke-DotNet @('run', '--project', $projectPath)
Write-Host 'Web Observation Quickstart verification passed.' -ForegroundColor Green
