[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'SiteAuditCli.csproj'
$packOutputDir = Join-Path $PSScriptRoot 'bin/pack-verify'
$toolInstallDir = Join-Path $PSScriptRoot 'bin/tool-verify'
$workDir = Join-Path $PSScriptRoot 'bin/audit-verify'
$siteDir = Join-Path $PSScriptRoot 'fixtures/site'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

foreach ($dir in @($packOutputDir, $toolInstallDir, $workDir)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Write-Host 'Packing the audit CLI as a dotnet tool...'
Invoke-DotNet @('pack', $projectPath, '--configuration', 'Release', '--output', $packOutputDir)

Write-Host 'Installing the packed tool into an isolated local tool directory...'
Invoke-DotNet @('tool', 'install', '--tool-path', $toolInstallDir, '--add-source', $packOutputDir, '--prerelease', 'AutomationSandbox.SiteAudit')

$toolExe = Join-Path $toolInstallDir 'automation-sandbox-audit'
if ($IsWindows) { $toolExe += '.exe' }

# A real HTTP origin is required: the crawler only follows http(s) links, so file:// fixtures cannot
# exercise it. A separate pwsh process serving the fixture keeps this script free of any Python/Node
# dependency, and a process (unlike a job) can always be ended.
$port = 18765
$siteRoot = [System.IO.Path]::GetFullPath($siteDir)
$pwshPath = (Get-Process -Id $PID).Path
$serverProcess = Start-Process -FilePath $pwshPath -PassThru -NoNewWindow `
    -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'fixtures/serve.ps1'), '-Root', $siteRoot, '-Port', $port)

try {
    $baseUrl = "http://localhost:$port"
    for ($i = 0; $i -lt 50; $i++) {
        try { Invoke-WebRequest -Uri "$baseUrl/index.html" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 200 }
        if ($i -eq 49) { throw 'The fixture web server did not start.' }
    }

    $dictionaryDir = Join-Path $workDir 'dictionaries'

    function Read-Issues {
        param([Parameter(Mandatory)][string] $ReportDir)
        $reportPath = Join-Path $ReportDir 'site-audit-report.json'
        $issues = @()
        foreach ($line in (Get-Content -LiteralPath $reportPath)) {
            if ($line.Trim().Length -gt 0) { $issues += ($line | ConvertFrom-Json).Issues }
        }
        return $issues
    }

    # 1. Planted typos, REAL downloaded Turkish + English dictionaries (also proves the pinned
    #    download URLs and the browser install work from a clean machine).
    Write-Host 'Auditing the fixture site with real tr + en dictionaries (expects findings, exit 1)...'
    & $toolExe --url "$baseUrl/index.html" --lang tr,en --allow-word Playwright --install-browser `
        --dictionary-dir $dictionaryDir --output-dir (Join-Path $workDir 'planted')
    if ($LASTEXITCODE -ne 1) { throw "Expected exit code 1 (findings) for the fixture site, got $LASTEXITCODE." }

    $issues = Read-Issues (Join-Path $workDir 'planted')
    $misspelled = @($issues | Where-Object { $_.IssueType -eq 'Spelling' } |
        ForEach-Object { [regex]::Match($_.Message, '"([^"]+)"').Groups[1].Value } | Sort-Object -Unique)
    $expectedMisspelled = @('Kullanici', 'Teşekürler', 'adinizi', 'recieve')
    $expectedSorted = ($expectedMisspelled | Sort-Object -Unique) -join ','
    if (($misspelled -join ',') -ne $expectedSorted) {
        throw "Misspelled words were [$($misspelled -join ', ')], expected exactly [$($expectedMisspelled -join ', ')]."
    }
    if (-not ($issues | Where-Object { $_.IssueType -eq 'DuplicateWord' })) {
        throw 'Expected the planted duplicate word ("the the") on the about page to be reported.'
    }
    $receive = $issues | Where-Object { $_.Message -like '*"recieve"*' } | Select-Object -First 1
    if ($receive.Message -notlike '*receive*') {
        throw "Expected an English suggestion for 'recieve' on this bilingual page, got: $($receive.Message)"
    }

    # 2. A clean page must come back clean: no finding on valid Turkish, valid English, or a word
    #    that only the second dictionary knows. Reuses the dictionaries downloaded above, offline.
    Write-Host 'Auditing the clean page offline (expects no findings, exit 0)...'
    & $toolExe --url "$baseUrl/clean/index.html" --lang tr,en --no-download `
        --dictionary-dir $dictionaryDir --output-dir (Join-Path $workDir 'clean')
    if ($LASTEXITCODE -ne 0) { throw "Expected exit code 0 (clean) for the clean page, got $LASTEXITCODE." }

    # 3. Failure modes must be exit 2, never a silent pass.
    Write-Host 'Checking that a missing dictionary with --no-download fails with exit 2...'
    & $toolExe --url "$baseUrl/clean/index.html" --lang de --no-download `
        --dictionary-dir (Join-Path $workDir 'empty') --output-dir (Join-Path $workDir 'missing-dict') 2>$null
    if ($LASTEXITCODE -ne 2) { throw "Expected exit code 2 for a missing dictionary, got $LASTEXITCODE." }

    Write-Host 'Checking that an unreachable start URL fails with exit 2...'
    & $toolExe --url 'http://localhost:1/' --lang en --no-download --dictionary-dir $dictionaryDir `
        --output-dir (Join-Path $workDir 'unreachable') 2>$null
    if ($LASTEXITCODE -ne 2) { throw "Expected exit code 2 for an unreachable URL, got $LASTEXITCODE." }

    Write-Host 'Site Audit CLI verification passed.' -ForegroundColor Green
    $global:LASTEXITCODE = 0
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue }
}
