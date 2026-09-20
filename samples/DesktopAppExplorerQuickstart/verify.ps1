[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'DesktopAppExplorerQuickstart.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Building Desktop App Explorer Quickstart sample...'
Invoke-DotNet @('build', $projectPath, '--configuration', 'Debug')

$builtExe = Join-Path $PSScriptRoot 'bin\Debug\net8.0-windows\DesktopAppExplorerQuickstart.exe'

# Character Map and Registry Editor ship with every windows-latest runner - no download,
# no network dependency - and are genuinely external, unmodified, third-party-authored
# Win32 applications, not this repo's own WinFormsApp/WpfApp demos. A green run here is
# real evidence that ApplicationConnector.Launch + UiTreeWalker.Discover work against
# arbitrary real UIA applications, not just ones this repository built and controls.
# Both are safe to open read-only: UiTreeWalker.Discover only reads the UI tree, and
# ApplicationConnector.Dispose closes (or kills) the launched process afterward.
$targets = @(
    (Join-Path $env:WINDIR 'System32\charmap.exe'),
    (Join-Path $env:WINDIR 'regedit.exe')
)

foreach ($target in $targets) {
    Write-Host "Running sample against $target..."
    & $builtExe $target
    if ($LASTEXITCODE -ne 0) {
        throw "Sample run against $target failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Desktop App Explorer Quickstart verification passed.' -ForegroundColor Green
