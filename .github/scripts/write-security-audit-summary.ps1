param(
    [Parameter(Mandatory = $true)]
    [string]$VulnerableReportPath,

    [Parameter(Mandatory = $true)]
    [string]$OutdatedReportPath,

    [Parameter(Mandatory = $true)]
    [string]$LegLabel,

    [string]$SummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Escape-MarkdownCell {
    param([string]$Value)

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Read-DotnetListReport {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }

    try {
        return $raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Could not parse '$Path' as JSON: $($_.Exception.Message)"
        return $null
    }
}

function Get-PropertyOrDefault {
    param(
        [object]$InputObject,
        [string]$Name,
        $Default = @()
    )

    if ($null -eq $InputObject) {
        return $Default
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Get-ProjectShortName {
    param([string]$ProjectPath)

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        return '(unknown project)'
    }

    return [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
}

if ([string]::IsNullOrWhiteSpace($SummaryPath)) {
    throw 'SummaryPath is required when GITHUB_STEP_SUMMARY is not set.'
}

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine("## Package security audit — $(Escape-MarkdownCell $LegLabel)")
[void]$markdown.AppendLine()

$vulnerableDocument = Read-DotnetListReport -Path $VulnerableReportPath
$vulnerableRows = @()
if ($null -eq $vulnerableDocument) {
    [void]$markdown.AppendLine('Vulnerability report unavailable (missing or unparsable `dotnet list package --vulnerable` output).')
    Write-Warning "No usable vulnerable-package report found at '$VulnerableReportPath'."
}
else {
    foreach ($project in @(Get-PropertyOrDefault -InputObject $vulnerableDocument -Name 'projects')) {
        $projectName = Get-ProjectShortName -ProjectPath $project.path
        foreach ($framework in @(Get-PropertyOrDefault -InputObject $project -Name 'frameworks')) {
            $packagesByKind = @(
                @{ Kind = 'Direct'; Packages = @(Get-PropertyOrDefault -InputObject $framework -Name 'topLevelPackages') }
                @{ Kind = 'Transitive'; Packages = @(Get-PropertyOrDefault -InputObject $framework -Name 'transitivePackages') }
            )
            foreach ($entry in $packagesByKind) {
                foreach ($package in $entry.Packages | Where-Object { $null -ne $_ }) {
                    foreach ($vulnerability in @(Get-PropertyOrDefault -InputObject $package -Name 'vulnerabilities')) {
                        $vulnerableRows += [PSCustomObject]@{
                            Project   = $projectName
                            Framework = [string]$framework.framework
                            Package   = [string]$package.id
                            Kind      = $entry.Kind
                            Severity  = [string]$vulnerability.severity
                            Resolved  = [string]$package.resolvedVersion
                            Advisory  = [string]$vulnerability.advisoryurl
                        }
                    }
                }
            }
        }
    }

    if ($vulnerableRows.Count -eq 0) {
        [void]$markdown.AppendLine('No known vulnerable packages found.')
    }
    else {
        $severityRank = @{ Critical = 0; High = 1; Moderate = 2; Low = 3 }
        $sortedRows = $vulnerableRows | Sort-Object @{
            Expression = { if ($severityRank.ContainsKey($_.Severity)) { $severityRank[$_.Severity] } else { 99 } }
        }, Project, Package

        [void]$markdown.AppendLine('| Project | Framework | Package | Type | Severity | Resolved | Advisory |')
        [void]$markdown.AppendLine('| :--- | :--- | :--- | :--- | :--- | :--- | :--- |')
        foreach ($row in $sortedRows) {
            $advisoryCell = if ([string]::IsNullOrWhiteSpace($row.Advisory)) { '' } else { "[link]($($row.Advisory))" }
            [void]$markdown.AppendLine(
                "| $(Escape-MarkdownCell $row.Project) | $(Escape-MarkdownCell $row.Framework) | " +
                "``$(Escape-MarkdownCell $row.Package)`` | $($row.Kind) | $($row.Severity) | " +
                "$(Escape-MarkdownCell $row.Resolved) | $advisoryCell |")
        }
    }
}

[void]$markdown.AppendLine()

$outdatedDocument = Read-DotnetListReport -Path $OutdatedReportPath
$outdatedRows = @()
if ($null -eq $outdatedDocument) {
    [void]$markdown.AppendLine('Outdated-package report unavailable (missing or unparsable `dotnet list package --outdated` output).')
    Write-Warning "No usable outdated-package report found at '$OutdatedReportPath'."
}
else {
    foreach ($project in @(Get-PropertyOrDefault -InputObject $outdatedDocument -Name 'projects')) {
        $projectName = Get-ProjectShortName -ProjectPath $project.path
        foreach ($framework in @(Get-PropertyOrDefault -InputObject $project -Name 'frameworks')) {
            foreach ($package in @(Get-PropertyOrDefault -InputObject $framework -Name 'topLevelPackages')) {
                $outdatedRows += [PSCustomObject]@{
                    Project   = $projectName
                    Framework = [string]$framework.framework
                    Package   = [string]$package.id
                    Requested = [string]$package.requestedVersion
                    Resolved  = [string]$package.resolvedVersion
                    Latest    = [string]$package.latestVersion
                }
            }
        }
    }

    [void]$markdown.AppendLine('<details><summary>Outdated packages</summary>')
    [void]$markdown.AppendLine()
    if ($outdatedRows.Count -eq 0) {
        [void]$markdown.AppendLine('All packages are up to date.')
    }
    else {
        [void]$markdown.AppendLine('| Project | Framework | Package | Requested | Resolved | Latest |')
        [void]$markdown.AppendLine('| :--- | :--- | :--- | :--- | :--- | :--- |')
        foreach ($row in $outdatedRows | Sort-Object Project, Package) {
            [void]$markdown.AppendLine(
                "| $(Escape-MarkdownCell $row.Project) | $(Escape-MarkdownCell $row.Framework) | " +
                "``$(Escape-MarkdownCell $row.Package)`` | $(Escape-MarkdownCell $row.Requested) | " +
                "$(Escape-MarkdownCell $row.Resolved) | $(Escape-MarkdownCell $row.Latest) |")
        }
    }
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('</details>')
}

Add-Content -LiteralPath $SummaryPath -Value $markdown.ToString() -Encoding utf8
