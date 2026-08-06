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
        foreach ($requiredEntry in @("README.md", "LICENSE", "$packageId.nuspec")) {
            if ($entryNames -notcontains $requiredEntry) {
                throw "$packagePath does not contain $requiredEntry"
            }
        }

        if (-not ($entryNames | Where-Object { $_ -like "lib/*.dll" })) {
            throw "$packagePath does not contain a library assembly"
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
