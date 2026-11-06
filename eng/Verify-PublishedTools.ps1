[CmdletBinding()]
param(
    # The exact version to verify, passed by release.yml as the just-published PACKAGE_VERSION.
    [Parameter(Mandatory)][string] $Version,
    # nuget.org by default. A local folder of packed tools lets the same checks run before anything is published.
    [string] $Source = 'https://api.nuget.org/v3/index.json'
)

# Post-publish consumer check for the agent, MCP and site-audit tools (#658), in the style of
# TestAutomation/SelfHealingCli/verify-published.ps1: install the EXACT version and run it. Only what needs no network,
# no key and no token is asserted, so the check cannot fail because a third party is down.

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.+-]+)?$') { throw "Not a version: $Version" }

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('automation-sandbox-tools-' + [Guid]::NewGuid().ToString('N'))
$maxAttempts = if ($env:VERIFY_PUBLISHED_MAX_ATTEMPTS) { [int]$env:VERIFY_PUBLISHED_MAX_ATTEMPTS } else { 30 }
$delaySeconds = if ($env:VERIFY_PUBLISHED_DELAY_SECONDS) { [int]$env:VERIFY_PUBLISHED_DELAY_SECONDS } else { 50 }

function Install-Tool {
    param([string] $PackageId, [string] $Command)
    $dir = Join-Path $root $Command
    New-Item -ItemType Directory -Path $dir | Out-Null
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        Write-Host "Install $PackageId $Version, attempt $attempt/$maxAttempts..."
        & dotnet tool install --tool-path $dir --version $Version --add-source $Source --no-cache $PackageId | Out-Host
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq $maxAttempts) { throw "$PackageId $Version could not be installed from $Source after $maxAttempts attempts. It may still be indexing; re-run the workflow, the push skips what already exists." }
        Start-Sleep -Seconds $delaySeconds
    }
    $exe = Join-Path $dir $Command
    if ($IsWindows) { $exe += '.exe' }
    return $exe
}

function Invoke-Tool {
    param([string] $Exe, [string[]] $Arguments = @(), [string] $StdIn = '')
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Exe
    foreach ($a in $Arguments) { $info.ArgumentList.Add($a) }
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($info)
    if ($StdIn) { $p.StandardInput.Write($StdIn) }
    $p.StandardInput.Close()
    $out = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Out = $out; Err = $err }
}

function Assert-That {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Check failed: $Message" }
    Write-Host "  ok: $Message"
}

try {
    New-Item -ItemType Directory -Path $root | Out-Null

    $agent = Install-Tool 'AutomationSandbox.Agent' 'automation-sandbox-agent'
    $r = Invoke-Tool $agent
    Assert-That ($r.ExitCode -eq 2) "the agent with no command exits 2 (could not run), got $($r.ExitCode)"
    $r = Invoke-Tool $agent @('triage', '--repo', 'owner/repo', '--issue', '1', '--token', 'x')
    Assert-That ($r.ExitCode -eq 2 -and -not $r.Out.Contains('x' + 'x')) "the agent refuses a secret-looking flag with exit 2"

    $audit = Install-Tool 'AutomationSandbox.SiteAudit' 'automation-sandbox-audit'
    $r = Invoke-Tool $audit @('--help')
    Assert-That ($r.ExitCode -eq 0 -and $r.Out -match '--url') "the audit tool prints its usage and exits 0"
    $r = Invoke-Tool $audit
    Assert-That ($r.ExitCode -eq 2) "the audit tool without --url exits 2, got $($r.ExitCode)"

    $mcp = Install-Tool 'AutomationSandbox.Mcp' 'automation-sandbox-mcp'
    $requests = (@(
        '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"verify","version":"1"}}}',
        '{"jsonrpc":"2.0","id":2,"method":"tools/list"}') -join "`n") + "`n"
    $r = Invoke-Tool $mcp @() $requests
    $lines = @($r.Out -split "`n" | Where-Object { $_.Trim() })
    Assert-That ($lines.Count -eq 2) "the MCP server answers exactly the two requests, got $($lines.Count) lines"
    $names = @(($lines[1] | ConvertFrom-Json).result.tools | ForEach-Object { $_.name })
    foreach ($expected in 'heal_locator', 'classify_ci_log', 'check_issue_eligibility', 'triage_issue_dry_run') {
        Assert-That ($names -contains $expected) "the MCP server lists the tool $expected"
    }

    Write-Host "Published-tool consumer verification passed for version $Version." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
