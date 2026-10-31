[CmdletBinding()]
param()

# Packs the agent CLI as a dotnet tool, installs the packed tool into an isolated directory and checks the exit
# codes that need no network, no key and no token (#585). The full path from an issue to a proposal is covered by the
# unit tests with a stub GitHub and fake models; a real run needs real keys and is not made in CI.

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'AgentCli.csproj'
$packOutputDir = Join-Path $PSScriptRoot 'bin/pack-verify'
$toolInstallDir = Join-Path $PSScriptRoot 'bin/tool-verify'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

foreach ($dir in @($packOutputDir, $toolInstallDir)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
}

Write-Host 'Packing the agent CLI as a dotnet tool...'
Invoke-DotNet @('pack', $projectPath, '--configuration', 'Release', '--output', $packOutputDir)

Write-Host 'Installing the packed tool into an isolated local tool directory...'
Invoke-DotNet @('tool', 'install', '--tool-path', $toolInstallDir, '--add-source', $packOutputDir, '--prerelease', 'AutomationSandbox.Agent')

$toolExe = Join-Path $toolInstallDir 'automation-sandbox-agent'
if ($IsWindows) { $toolExe += '.exe' }

# Runs the tool with exactly the given environment (none of the runner's own keys leak in) and returns exit code,
# stdout and stderr.
function Invoke-Tool {
    param([string[]] $Arguments = @(), [hashtable] $Environment = @{})

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $toolExe
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    foreach ($name in @('PATH', 'HOME', 'DOTNET_ROOT', 'DOTNET_CLI_HOME', 'USERPROFILE', 'SystemRoot', 'TEMP', 'TMP')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $startInfo.Environment[$name] = $value }
    }
    foreach ($key in $Environment.Keys) { $startInfo.Environment[$key] = [string]$Environment[$key] }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Out = $stdout; Err = $stderr }
}

function Assert-That {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Check failed: $Message" }
    Write-Host "  ok: $Message"
}

$projectId = 'PVT_kwHOAr3aNM4BgUrT'
$issueArgs = @('triage', '--repo', 'owner/repo', '--issue', '7')

$help = Invoke-Tool @('--help')
Assert-That ($help.ExitCode -eq 0) '--help exits 0'
Assert-That ($help.Out -match 'PROJECT_TOKEN' -and $help.Out -match 'Exit codes') '--help names the environment and the exit codes'

$none = Invoke-Tool
Assert-That ($none.ExitCode -eq 2) 'no command exits 2'

$secret = Invoke-Tool ($issueArgs + @('--token', 'super-secret-value'))
Assert-That ($secret.ExitCode -eq 2) 'a token passed as an argument exits 2'
Assert-That (-not ($secret.Out + $secret.Err).Contains('super-secret-value')) 'the rejected token is never echoed'

$badRepo = Invoke-Tool @('triage', '--repo', 'not-a-repo', '--issue', '7')
Assert-That ($badRepo.ExitCode -eq 2) 'an invalid repository exits 2'

$noToken = Invoke-Tool $issueArgs @{ PROJECT_ID = $projectId; GEMINI_API_KEY = 'x' }
Assert-That ($noToken.ExitCode -eq 2 -and $noToken.Err -match 'PROJECT_TOKEN is required') 'a missing PROJECT_TOKEN exits 2 before any request'

$noModel = Invoke-Tool $issueArgs @{ PROJECT_ID = $projectId; PROJECT_TOKEN = 'x' }
Assert-That ($noModel.ExitCode -eq 2 -and $noModel.Err -match 'No model is configured') 'no model exits 2 before any request'

Write-Host 'Agent CLI verification passed.'
