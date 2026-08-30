[CmdletBinding()]
param()

# Per-PR sample compatibility check (#336).
#
# This proves the sample's own code (Program.cs) still compiles and runs against
# the CURRENT engine source, by building a copy of it with the published
# PackageReference swapped for a ProjectReference. It deliberately does NOT touch
# nuget.org: a per-PR check must never depend on a version that is only published
# after the release workflow runs (the chicken-and-egg #336 fixes).
#
# The real "does the published package work for a consumer" check now lives in
# release.yml as a post-publish step (verify-published.ps1).

$ErrorActionPreference = 'Stop'
$sourceProjectPath = Join-Path $PSScriptRoot 'HeuristicHealingQuickstart.csproj'
$sourceProgramPath = Join-Path $PSScriptRoot 'Program.cs'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$selfHealingProj = Join-Path $repoRoot 'TestAutomation/SelfHealing/SelfHealing.csproj'

if (-not (Test-Path -LiteralPath $selfHealingProj)) {
    throw "Expected AutomationSandbox.SelfHealing project at $selfHealingProj."
}

$projectText = Get-Content -LiteralPath $sourceProjectPath -Raw
if ($projectText -notmatch '<PackageReference\s+Include="AutomationSandbox\.SelfHealing"') {
    throw 'The committed sample must consume AutomationSandbox.SelfHealing as a PackageReference (it doubles as the published-consumer example).'
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('automation-sandbox-source-compat-' + [Guid]::NewGuid().ToString('N'))
$consumerPath = Join-Path $verificationRoot 'consumer'
$projectPath = Join-Path $consumerPath 'HeuristicHealingQuickstart.csproj'

try {
    New-Item -ItemType Directory -Path $consumerPath | Out-Null
    Copy-Item -LiteralPath $sourceProgramPath -Destination (Join-Path $consumerPath 'Program.cs')

    # Swap the published PackageReference for a ProjectReference to the in-repo engine.
    $rewritten = $projectText -replace `
        '<PackageReference\s+Include="AutomationSandbox\.SelfHealing"\s+Version="[^"]+"\s*/>', `
        ('<ProjectReference Include="' + $selfHealingProj + '" />')
    Set-Content -LiteralPath $projectPath -Value $rewritten -NoNewline

    Write-Host 'Building the sample against the current engine source (ProjectReference)...'
    Invoke-DotNet @('build', $projectPath, '--configuration', 'Release')

    Write-Host 'Running the end-to-end healing scenario...'
    Invoke-DotNet @('run', '--project', $projectPath, '--configuration', 'Release', '--no-build')

    Write-Host 'Sample source-compatibility verification passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}
