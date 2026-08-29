#!/usr/bin/env pwsh
# Lightweight DORA & engine-reliability metrics calculator for Automation Sandbox.
#
# Computes, over a rolling window:
#   1. Deployment Frequency (DF)      - GitHub Releases published in the window
#   2. Lead Time for Changes (LTTC)   - PR created -> merged, for PRs merged in the window
#   3. Change Failure Rate (CFR)      - share of releases/merges that needed a hotfix
#   4. Time to Restore Service (MTTR) - bug/correctness issue created -> closed, in the window
#
# It also surfaces the self-healing engine's configured reliability gates. Those are
# configuration, not measurements: the values are read straight out of SimilarityWeights.cs
# so the report cannot silently drift from the code.
#
# No external services. Reads the public GitHub REST API (a token lifts the rate limit).
# On a fetch failure the script exits non-zero rather than emitting a report built on
# empty data - a green "Elite" report drawn from a failed API call is worse than none.
#
# PowerShell rather than Python: this repository keeps its tooling to C# and pwsh
# (samples/*/verify.ps1, eng/, .github/scripts/) - see AGENTS.md.

[CmdletBinding()]
param(
    [string]$Repo = "mustafasercansak/automation-sandbox",
    [int]$Days = 30,
    [ValidateSet("json", "markdown")][string]$Format = "markdown",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

class GitHubFetchException : System.Exception {
    GitHubFetchException([string]$message) : base($message) { }
}

function Parse-Ts([string]$Value) {
    # GitHub ISO-8601 timestamps are always UTC with a 'Z' suffix.
    return [DateTimeOffset]::Parse($Value, [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AssumeUniversal)
}

function Fetch-All([string]$Endpoint, [string]$Token) {
    # Fetch every page of a list endpoint, following the RFC-5988 Link header.
    $headers = @{
        "User-Agent" = "DORA-Metrics-Calculator"
        "Accept"     = "application/vnd.github+json"
    }
    if ($Token) { $headers["Authorization"] = "token $Token" }

    $url = "https://api.github.com/repos/$Repo/${Endpoint}?state=all&per_page=100&sort=created&direction=desc"
    $items = [System.Collections.Generic.List[object]]::new()
    while ($url) {
        try {
            $response = Invoke-WebRequest -Uri $url -Headers $headers -TimeoutSec 30 -UseBasicParsing
        }
        catch {
            throw [GitHubFetchException]::new("GitHub API request failed for '$Endpoint': $($_.Exception.Message)")
        }

        $page = $response.Content | ConvertFrom-Json
        if ($page -isnot [System.Array] -and $null -ne $page) {
            throw [GitHubFetchException]::new(
                "GitHub API returned $($page.GetType().Name), not a list, for '${Endpoint}': " +
                ("$($page | ConvertTo-Json -Compress)".Substring(0, [Math]::Min(200, "$($page | ConvertTo-Json -Compress)".Length))))
        }
        if ($null -ne $page) { $items.AddRange([object[]]$page) }

        # Follow the Link header to the end. We can't stop early on created-desc order:
        # a PR created long ago can still have merged within the window.
        $nextMatch = [regex]::Match("$($response.Headers.Link)", '<([^>]+)>;\s*rel="next"')
        $url = if ($nextMatch.Success) { $nextMatch.Groups[1].Value } else { $null }
    }
    return $items
}

function Read-EngineGates() {
    # Read the self-healing gate thresholds straight from SimilarityWeights.cs so the
    # report stays honest if the defaults change. These are configuration, not measured
    # outcomes.
    $gates = [ordered]@{
        minimum_confidence       = $null
        minimum_evidence_weight  = $null
        minimum_consensus_votes  = $null
    }
    $weightsPath = Join-Path $PSScriptRoot ".." "TestAutomation" "SelfHealing" "SimilarityWeights.cs"
    if (-not (Test-Path $weightsPath)) { return $gates }

    $src = [System.IO.File]::ReadAllText($weightsPath)
    $patterns = [ordered]@{
        minimum_confidence      = 'MinimumConfidence\s*\{\s*get;\s*set;\s*\}\s*=\s*([0-9.]+)'
        minimum_evidence_weight = 'MinimumEvidenceWeight\s*\{\s*get;\s*set;\s*\}\s*=\s*([0-9.]+)'
        minimum_consensus_votes = 'MinimumConsensusVotes\s*\{\s*get;\s*set;\s*\}\s*=\s*([0-9]+)'
    }
    foreach ($key in $patterns.Keys) {
        $m = [regex]::Match($src, $patterns[$key])
        if ($m.Success) { $gates[$key] = $m.Groups[1].Value }
    }
    return $gates
}

function Test-IsHotfix($Pr) {
    $ref = "$($Pr.head.ref)".ToLowerInvariant()
    $title = "$($Pr.title)".ToLowerInvariant()
    $labels = @($Pr.labels | ForEach-Object { "$($_.name)".ToLowerInvariant() })
    return (
        $ref.StartsWith("hotfix/") -or
        $title.Contains("hotfix") -or
        $title.StartsWith("revert ") -or
        $labels -contains "regression" -or
        $labels -contains "release-blocker"
    )
}

$token = if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }

try {
    $prs = Fetch-All "pulls" $token
    $releases = Fetch-All "releases" $token
    $rawIssues = Fetch-All "issues" $token
}
catch [GitHubFetchException] {
    [Console]::Error.WriteLine("error: $($_.Exception.Message)")
    exit 1
}

# The issues endpoint also returns pull requests; drop them so PRs are not
# double-counted as "restored bugs" in MTTR.
$issues = @($rawIssues | Where-Object { -not $_.PSObject.Properties["pull_request"] })

$now = [DateTimeOffset]::UtcNow
$cutoff = $now.AddDays(-$Days)

# 1. Deployment Frequency
$recentReleases = @($releases | Where-Object { $_.published_at -and (Parse-Ts $_.published_at) -ge $cutoff })
$dfCount = $recentReleases.Count
if ($dfCount -ge 4) { $dfRating = "Elite (weekly or more)" }
elseif ($dfCount -ge 1) { $dfRating = "High (monthly)" }
else { $dfRating = "No releases in window" }

# 2. Lead Time for Changes
$mergedPrs = @($prs | Where-Object { $_.merged_at -and (Parse-Ts $_.merged_at) -ge $cutoff })
$leadTimesHours = @($mergedPrs | ForEach-Object { ((Parse-Ts $_.merged_at) - (Parse-Ts $_.created_at)).TotalHours })
$totalMerged = $mergedPrs.Count
$avgLeadTime = if ($leadTimesHours.Count -gt 0) { ($leadTimesHours | Measure-Object -Average).Average } else { $null }
if ($null -eq $avgLeadTime) { $lttcRating = "n/a - no PRs merged in window" }
elseif ($avgLeadTime -le 4) { $lttcRating = "Elite (< 4 hours)" }
elseif ($avgLeadTime -le 24) { $lttcRating = "High (< 1 day)" }
elseif ($avgLeadTime -le 168) { $lttcRating = "Medium (< 1 week)" }
else { $lttcRating = "Low (> 1 week)" }

# 3. Change Failure Rate: of the changes shipped, how many needed a follow-up
# hotfix. Approximated by PRs that are explicitly a hotfix or a regression - not
# every bug-fix PR (in this repo almost every PR is a bug fix by design, so that
# would carry no signal).
$hotfixPrs = @($mergedPrs | Where-Object { Test-IsHotfix $_ })
$cfrPercentage = if ($totalMerged -gt 0) { $hotfixPrs.Count / $totalMerged * 100.0 } else { $null }
if ($null -eq $cfrPercentage) { $cfrRating = "n/a - no PRs merged in window" }
elseif ($cfrPercentage -le 15.0) { $cfrRating = "Elite (0-15%)" }
elseif ($cfrPercentage -le 30.0) { $cfrRating = "High (16-30%)" }
else { $cfrRating = "Low (> 30%)" }

# 4. Time to Restore Service
$closedBugs = @($issues | Where-Object {
    $_.state -eq "closed" -and $_.closed_at -and
    ($_.labels | Where-Object { $_.name -in @("bug", "correctness") }) -and
    (Parse-Ts $_.closed_at) -ge $cutoff
})
$mttrHours = @($closedBugs | ForEach-Object { ((Parse-Ts $_.closed_at) - (Parse-Ts $_.created_at)).TotalHours })
$avgMttr = if ($mttrHours.Count -gt 0) { ($mttrHours | Measure-Object -Average).Average } else { $null }
if ($null -eq $avgMttr) { $mttrRating = "n/a - no bug issues closed in window" }
elseif ($avgMttr -le 24) { $mttrRating = "Elite (< 1 day)" }
elseif ($avgMttr -le 168) { $mttrRating = "High (< 1 week)" }
else { $mttrRating = "Medium (> 1 week)" }

$metrics = [ordered]@{
    period_days   = $Days
    generated_at  = $now.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
    deployment_frequency = [ordered]@{
        releases_count = $dfCount
        rating         = $dfRating
    }
    lead_time_for_changes = [ordered]@{
        merged_prs_count = $totalMerged
        average_hours    = if ($null -ne $avgLeadTime) { [Math]::Round($avgLeadTime, 2) } else { $null }
        rating           = $lttcRating
    }
    change_failure_rate = [ordered]@{
        hotfix_prs_count = $hotfixPrs.Count
        percentage       = if ($null -ne $cfrPercentage) { [Math]::Round($cfrPercentage, 1) } else { $null }
        rating           = $cfrRating
    }
    time_to_restore = [ordered]@{
        resolved_bugs_count = $closedBugs.Count
        average_hours       = if ($null -ne $avgMttr) { [Math]::Round($avgMttr, 2) } else { $null }
        rating              = $mttrRating
    }
    engine_reliability_gates = Read-EngineGates
}

function Format-Cell($Value, [string]$Suffix = "") {
    if ($null -ne $Value) { return "$Value$Suffix" }
    return "n/a"
}

if ($Format -eq "json") {
    $content = $metrics | ConvertTo-Json -Depth 5
}
else {
    $df = $metrics.deployment_frequency
    $lttc = $metrics.lead_time_for_changes
    $cfr = $metrics.change_failure_rate
    $ttr = $metrics.time_to_restore
    $gates = $metrics.engine_reliability_gates

    $content = @"
# DORA & Engine Reliability Report

**Assessment window:** last $($metrics.period_days) days  
**Generated:** $($metrics.generated_at)

## Core DORA Metrics

| Metric | Measured value | Rating |
| :--- | :---: | :--- |
| Deployment Frequency | $($df.releases_count) releases | $($df.rating) |
| Lead Time for Changes | $(Format-Cell $lttc.average_hours ' hrs') ($($lttc.merged_prs_count) PRs) | $($lttc.rating) |
| Change Failure Rate | $(if ($null -ne $cfr.percentage) { $cfr.percentage.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture) + '%' } else { 'n/a' }) ($($cfr.hotfix_prs_count) hotfix PRs) | $($cfr.rating) |
| Time to Restore Service | $(Format-Cell $ttr.average_hours ' hrs') ($($ttr.resolved_bugs_count) bugs) | $($ttr.rating) |

## Self-Healing Reliability Gates

_Configuration read from ``SimilarityWeights.cs`` - thresholds in effect, not measured outcomes._

| Gate | Configured value |
| :--- | :---: |
| Minimum confidence | $(Format-Cell $gates.minimum_confidence) |
| Minimum evidence weight | $(Format-Cell $gates.minimum_evidence_weight) |
| Minimum consensus votes | $(Format-Cell $gates.minimum_consensus_votes) |
"@
}

if ($Output) {
    $fullPath = [System.IO.Path]::GetFullPath($Output)
    $outDir = [System.IO.Path]::GetDirectoryName($fullPath)
    if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    [System.IO.File]::WriteAllText($fullPath, $content + "`n")
    Write-Host "Report written to $Output"
}
else {
    Write-Output $content
}
exit 0
