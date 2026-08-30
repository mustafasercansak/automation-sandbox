param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = "Stop"

$packageDirectoryPath = (Resolve-Path -LiteralPath $PackageDirectory -ErrorAction Stop).Path
$packageIds = @(
    "AutomationSandbox.UiModel",
    "AutomationSandbox.SelfHealing",
    "AutomationSandbox.LlmHealing",
    "AutomationSandbox.Discovery",
    "AutomationSandbox.WebDiscovery",
    "AutomationSandbox.IntentAutomation",
    "AutomationSandbox.PlaywrightLiveExploration"
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($packageId in $packageIds) {
    $packagePath = Join-Path $packageDirectoryPath "$packageId.$Version.nupkg"
    $symbolPackagePath = Join-Path $packageDirectoryPath "$packageId.$Version.snupkg"

    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Missing NuGet package: $packagePath"
    }

    if (-not (Test-Path -LiteralPath $symbolPackagePath -PathType Leaf)) {
        throw "Missing symbol package: $symbolPackagePath"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($requiredEntry in @("README.md", "LICENSE", "icon.png", "$packageId.nuspec")) {
            if ($entryNames -notcontains $requiredEntry) {
                throw "$packagePath does not contain $requiredEntry"
            }
        }

        if (-not ($entryNames | Where-Object { $_ -like "lib/*.dll" })) {
            throw "$packagePath does not contain a library assembly"
        }

        # The embedded README must be the per-package landing page (docs/nuget/README.<Name>.md),
        # not the monorepo root README that would fall back in via Directory.Build.props (#338).
        # The per-package files open with "# <PackageId>"; the root README opens with
        # "# Automation Sandbox" and carries LaTeX/mermaid that nuget.org cannot render.
        $readmeEntry = $archive.Entries | Where-Object { $_.FullName -eq "README.md" } | Select-Object -First 1
        $reader = New-Object System.IO.StreamReader($readmeEntry.Open())
        try {
            $readmeText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $firstHeading = ($readmeText -split "`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim()
        if ($firstHeading -ne "# $packageId") {
            throw "$packagePath embeds the wrong README (first heading '$firstHeading', expected '# $packageId'). Add docs/nuget/README.$($packageId -replace '^AutomationSandbox\.', '').md."
        }

        if ($readmeText -match '(?m)^\s*```mermaid' -or $readmeText -match '\$\$' -or $readmeText -match '\$\\') {
            throw "$packagePath README contains LaTeX or mermaid syntax that nuget.org does not render."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$packages = @(Get-ChildItem -LiteralPath $packageDirectoryPath -Filter "*.nupkg" -File)
$symbolPackages = @(Get-ChildItem -LiteralPath $packageDirectoryPath -Filter "*.snupkg" -File)
if ($packages.Count -ne $packageIds.Count -or $symbolPackages.Count -ne $packageIds.Count) {
    throw "Expected $($packageIds.Count) .nupkg and .snupkg files; found $($packages.Count) and $($symbolPackages.Count)."
}

Write-Host "Validated $($packageIds.Count) NuGet packages and symbol packages for version $Version."
