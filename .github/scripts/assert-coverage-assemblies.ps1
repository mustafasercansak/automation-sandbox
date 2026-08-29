param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageSearchPath,

    [Parameter(Mandatory = $true)]
    [string[]]$RequiredAssembly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# #289 regression guard. The net48 testhost once produced a green run and a valid
# coverage.cobertura.xml that silently omitted the net48-only Discovery assembly - the
# whole reason the Windows coverage leg exists. write-coverage-summary.ps1 renders
# whatever it finds without complaint, so this script is the hard gate: fail the job if
# any expected assembly is absent from the Cobertura report(s).

if (-not (Test-Path -LiteralPath $CoverageSearchPath -PathType Container)) {
    throw "Coverage search path '$CoverageSearchPath' does not exist - the coverage collection step did not run."
}

# Accept both a real array (pwsh command mode: -RequiredAssembly A,B,C) and a single
# comma-joined string (pwsh -File mode passes the literal "A,B,C").
$required = @(
    $RequiredAssembly |
        ForEach-Object { $_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -ne '' }
)
if ($required.Count -eq 0) {
    throw 'RequiredAssembly resolved to an empty list.'
}

# Same discovery rule as write-coverage-summary.ps1: real reports only, not VSTest
# attachment staging copies under an '\In\' directory.
$reportFiles = @(
    Get-ChildItem -LiteralPath $CoverageSearchPath -Filter 'coverage.cobertura.xml' -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/]In[\\/]' }
)

if ($reportFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml found under '$CoverageSearchPath'."
}

$present = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($file in $reportFiles) {
    [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
    $coverage = $document.coverage
    if ($null -eq $coverage -or $null -eq $coverage.packages) {
        continue
    }

    foreach ($package in @($coverage.packages.package)) {
        if ($null -ne $package) {
            $name = [string]$package.name
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                [void]$present.Add($name)
            }
        }
    }
}

$missing = @($required | Where-Object { -not $present.Contains($_) })
if ($missing.Count -gt 0) {
    $presentList = (@($present) | Sort-Object) -join ', '
    throw ("Windows coverage report is missing expected assemblies: $($missing -join ', '). " +
        "Present: $presentList. Dynamic instrumentation likely dropped a net48-only module - " +
        "the #289 Discovery gap has reopened.")
}

Write-Host "Windows coverage report contains all $($required.Count) expected assemblies: $($required -join ', ')."
