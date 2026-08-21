param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageSearchPath,

    [Parameter(Mandatory = $true)]
    [string]$LegLabel,

    [string]$PlatformNote,

    [string]$SummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Format-Rate {
    param([string]$Value)

    $rate = 0.0
    if (-not [double]::TryParse(
        $Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$rate)) {
        throw "Coverage rate '$Value' is not a valid invariant-culture number."
    }

    return ($rate * 100.0).ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture) + '%'
}

function Escape-MarkdownCell {
    param([string]$Value)

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

if ([string]::IsNullOrWhiteSpace($SummaryPath)) {
    throw 'SummaryPath is required when GITHUB_STEP_SUMMARY is not set.'
}

$reports = @()
if (Test-Path -LiteralPath $CoverageSearchPath -PathType Container) {
    $reports = @(
        Get-ChildItem -LiteralPath $CoverageSearchPath -Filter 'coverage.cobertura.xml' -File -Recurse |
            Sort-Object FullName
    )
}

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine("## Code coverage — $(Escape-MarkdownCell $LegLabel)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('> Visibility only. Windows and Linux measure different executable surfaces; figures are not combined and no coverage threshold is enforced.')
[void]$markdown.AppendLine()
if (-not [string]::IsNullOrWhiteSpace($PlatformNote)) {
    [void]$markdown.AppendLine("> Platform note: $(Escape-MarkdownCell $PlatformNote)")
    [void]$markdown.AppendLine()
}

if ($reports.Count -eq 0) {
    [void]$markdown.AppendLine('Coverage report unavailable. The test result and uploaded artifacts remain the source of failure diagnostics.')
    Write-Warning "No coverage.cobertura.xml file was found under '$CoverageSearchPath'."
}
else {
    $showReportColumn = $reports.Count -gt 1
    if ($showReportColumn) {
        [void]$markdown.AppendLine('| Report | Scope | Line coverage | Branch coverage |')
        [void]$markdown.AppendLine('| :--- | :--- | ---: | ---: |')
    }
    else {
        [void]$markdown.AppendLine('| Scope | Line coverage | Branch coverage |')
        [void]$markdown.AppendLine('| :--- | ---: | ---: |')
    }

    foreach ($report in $reports) {
        [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
        $coverage = $document.coverage
        if ($null -eq $coverage) {
            throw "Coverage file '$($report.FullName)' has no <coverage> root element."
        }

        $reportName = Escape-MarkdownCell $report.Directory.Name
        $overallLines = "$(Format-Rate $coverage.'line-rate') ($($coverage.'lines-covered')/$($coverage.'lines-valid'))"
        $overallBranches = "$(Format-Rate $coverage.'branch-rate') ($($coverage.'branches-covered')/$($coverage.'branches-valid'))"
        if ($showReportColumn) {
            [void]$markdown.AppendLine("| $reportName | **Overall** | $overallLines | $overallBranches |")
        }
        else {
            [void]$markdown.AppendLine("| **Overall** | $overallLines | $overallBranches |")
        }

        $packages = @($coverage.packages.package) | Sort-Object name
        foreach ($package in $packages) {
            $assembly = Escape-MarkdownCell ([string]$package.name)
            $lineRate = Format-Rate ([string]$package.'line-rate')
            $branchRate = Format-Rate ([string]$package.'branch-rate')
            if ($showReportColumn) {
                [void]$markdown.AppendLine("| $reportName | ``$assembly`` | $lineRate | $branchRate |")
            }
            else {
                [void]$markdown.AppendLine("| ``$assembly`` | $lineRate | $branchRate |")
            }
        }
    }
}

Add-Content -LiteralPath $SummaryPath -Value $markdown.ToString() -Encoding utf8
