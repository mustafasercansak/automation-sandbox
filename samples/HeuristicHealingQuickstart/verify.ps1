[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceProjectPath = Join-Path $PSScriptRoot 'HeuristicHealingQuickstart.csproj'
$sourceProgramPath = Join-Path $PSScriptRoot 'Program.cs'
$projectText = Get-Content -LiteralPath $sourceProjectPath -Raw

if ($projectText -match '<ProjectReference') {
    throw 'Consumer verification must use published PackageReference entries, not repository ProjectReference entries.'
}

$directoryBuildPropsPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $directoryBuildPropsPath)) {
    $directoryBuildPropsPath = Join-Path $PSScriptRoot '../../Directory.Build.props'
}

$propsText = Get-Content -LiteralPath $directoryBuildPropsPath -Raw
if ($propsText -match '<Version>(?<version>[^<]+)</Version>') {
    $expectedVersion = $Matches['version'].Trim()
} else {
    throw 'Could not extract <Version> from Directory.Build.props.'
}

$escapedVersion = [regex]::Escape($expectedVersion)
if ($projectText -notmatch "PackageReference Include=`"AutomationSandbox\.SelfHealing`" Version=`"$escapedVersion`"") {
    throw "The sample must pin the published AutomationSandbox.SelfHealing $expectedVersion package (matching Directory.Build.props)."
}

$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('automation-sandbox-consumer-' + [Guid]::NewGuid().ToString('N'))
$consumerPath = Join-Path $verificationRoot 'consumer'
$packagesPath = Join-Path $verificationRoot 'packages'
$projectPath = Join-Path $consumerPath 'HeuristicHealingQuickstart.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    New-Item -ItemType Directory -Path $consumerPath | Out-Null
    Copy-Item -LiteralPath $sourceProjectPath -Destination $projectPath
    Copy-Item -LiteralPath $sourceProgramPath -Destination (Join-Path $consumerPath 'Program.cs')

    Write-Host 'Restoring an isolated consumer copy exclusively from nuget.org into a clean package directory...'
    Invoke-DotNet @(
        'restore', $projectPath,
        '--force',
        '--no-cache',
        '--packages', $packagesPath,
        '--source', 'https://api.nuget.org/v3/index.json'
    )

    Write-Host 'Building the consumer sample...'
    Invoke-DotNet @('build', $projectPath, '--configuration', 'Release', '--no-restore')

    Write-Host 'Running the end-to-end healing scenario...'
    Invoke-DotNet @('run', '--project', $projectPath, '--configuration', 'Release', '--no-build')

    Write-Host 'Published-package consumer verification passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}
