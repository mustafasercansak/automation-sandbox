param(
    [Parameter(Mandatory = $true)]
    [string]$AblationSearchPath,

    [Parameter(Mandatory = $true)]
    [string]$LegLabel,

    [string]$SummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Format-Percent {
    param([double]$Value)
    return ($Value * 100.0).ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture) + '%'
}

function Escape-MarkdownCell {
    param([string]$Value)
    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

if ([string]::IsNullOrWhiteSpace($SummaryPath)) {
    throw 'SummaryPath is required when GITHUB_STEP_SUMMARY is not set.'
}

$files = @()
if (Test-Path -LiteralPath $AblationSearchPath) {
    $files = @(Get-ChildItem -LiteralPath $AblationSearchPath -Filter 'ablation-metrics-*.json' -File -Recurse | Sort-Object Name)
}

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine("## 🔬 Locator Ablation & Calibration Telemetry — $(Escape-MarkdownCell $LegLabel)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('> Tracks false-heal rate, compound-drift recall, and manual review rate per commit across real application benchmark fixtures.')
[void]$markdown.AppendLine()

if ($files.Count -eq 0) {
    [void]$markdown.AppendLine('Ablation metrics telemetry file was not found for this run leg.')
    Write-Warning "No ablation-metrics-*.json files found under '$AblationSearchPath'."
}
else {
    [void]$markdown.AppendLine('| Benchmark Dataset | Scenarios | Precision | Auto-Heal Recall | Compound-Drift Recall | False-Heal Rate | Manual Review Rate | Correct / False / Missed / Correct Decline / Removed False |')
    [void]$markdown.AppendLine('| :--- | ---: | ---: | ---: | ---: | ---: | ---: | :--- |')

    foreach ($file in $files) {
        $jsonContent = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $datasetName = Escape-MarkdownCell ([string]$jsonContent.DatasetName)
        $total = [int]$jsonContent.TotalScenarios
        $precision = Format-Percent ([double]$jsonContent.Precision)
        $recall = Format-Percent ([double]$jsonContent.AutoHealRecall)
        $compoundRecall = Format-Percent ([double]$jsonContent.CompoundDriftRecall)
        $falseHealRate = Format-Percent ([double]$jsonContent.FalseHealRate)
        $reviewRate = Format-Percent ([double]$jsonContent.ManualReviewRate)

        $breakdown = "$($jsonContent.CorrectHeals) / $($jsonContent.FalseHeals) / $($jsonContent.MissedHeals) / $($jsonContent.CorrectDeclines) / $($jsonContent.FalseHealsOnRemoved)"

        [void]$markdown.AppendLine("| **$datasetName** | $total | $precision | $recall | $compoundRecall | $falseHealRate | $reviewRate | $breakdown |")
    }
}

[void]$markdown.AppendLine()
Add-Content -LiteralPath $SummaryPath -Value $markdown.ToString() -Encoding utf8
