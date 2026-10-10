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

function Invoke-SampleAgainst {
    param([Parameter(Mandatory)][string] $Target)

    Write-Host "Running sample against $Target..."
    & $builtExe $Target
    if ($LASTEXITCODE -ne 0) {
        throw "Sample run against $Target failed with exit code $LASTEXITCODE."
    }
}

# Character Map and Registry Editor ship with every windows-latest runner - no download,
# no network dependency - and are genuinely external, unmodified, third-party-authored
# Win32 applications, not this repo's own WinFormsApp/WpfApp demos. A green run here is
# real evidence that ApplicationConnector.Launch + UiTreeWalker.Discover work against
# arbitrary real UIA applications, not just ones this repository built and controls.
# Both are safe to open read-only: UiTreeWalker.Discover only reads the UI tree, and
# ApplicationConnector.Dispose closes (or kills) the launched process afterward.
Invoke-SampleAgainst (Join-Path $env:WINDIR 'System32\charmap.exe')
Invoke-SampleAgainst (Join-Path $env:WINDIR 'regedit.exe')

# Two downloaded, actively-maintained, genuinely third-party Windows applications with
# richer UI trees (tabbed documents, toolbars, tree/list views) than the two built-in
# tools above - the best additional external-app evidence achievable without a Windows
# machine of our own (#501; #489 - a maintainer's own workplace application - is still
# the thing this cannot substitute for). Both ship as a no-install portable zip with no
# EULA/first-run dialog to dismiss, downloaded directly from their own GitHub Releases
# and pinned to a specific version so an unrelated future upstream release can't change
# what this verifies. If a pinned asset URL ever 404s because the upstream project
# pruned old releases, bump the version below - don't just delete the app and lose the
# coverage silently.
$downloadDir = Join-Path $PSScriptRoot 'external-apps'
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

$nppVersion = '8.9.8.1'
$nppZip = Join-Path $downloadDir 'npp.zip'
$nppDir = Join-Path $downloadDir 'npp'
Invoke-WebRequest -Uri "https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v$nppVersion/npp.$nppVersion.portable.x64.zip" -OutFile $nppZip
Expand-Archive -Path $nppZip -DestinationPath $nppDir -Force
Invoke-SampleAgainst (Join-Path $nppDir 'notepad++.exe')

$peazipVersion = '11.3.0'
$peazipZip = Join-Path $downloadDir 'peazip.zip'
$peazipDir = Join-Path $downloadDir 'peazip'
Invoke-WebRequest -Uri "https://github.com/peazip/PeaZip/releases/download/$peazipVersion/peazip_portable-$peazipVersion.WIN64.zip" -OutFile $peazipZip
Expand-Archive -Path $peazipZip -DestinationPath $peazipDir -Force
Invoke-SampleAgainst (Join-Path $peazipDir "peazip_portable-$peazipVersion.WIN64\peazip.exe")

Write-Host 'Desktop App Explorer Quickstart verification passed.' -ForegroundColor Green
