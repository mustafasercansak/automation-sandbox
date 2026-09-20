[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'SiteContentAuditQuickstart.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Building and running Site Content Audit Quickstart sample (bundled fixture site)...'
Invoke-DotNet @('run', '--project', $projectPath)
Write-Host 'Site Content Audit Quickstart verification passed.' -ForegroundColor Green
