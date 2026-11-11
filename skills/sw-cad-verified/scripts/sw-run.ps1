# sw-run.ps1 - run a SolidWorks automation script under a timeout, and guarantee
# that a sweep runs afterwards EVEN IF the child had to be killed.
#
# Why: a script that is killed by a timeout never executes its own finally block,
# so sw.ExitApp() never runs and a hidden instance is orphaned. This wrapper is
# the parent: it survives the kill, so the sweep always happens.
#
# USAGE
#   powershell -NoProfile -ExecutionPolicy Bypass -File sw-run.ps1 `
#       -Script %DSH_WORKSPACE%\cae\part_read\run_reader.ps1 `
#       -ScriptArgs '-Part','D:\x\part.SLDPRT' `
#       -TimeoutSec 300
#
# Exit code = child exit code (or 124 on timeout).
param(
    [Parameter(Mandatory=$true)][string]$Script,
    [string[]]$ScriptArgs = @(),
    [int]$TimeoutSec = 600,
    [switch]$NoSweep,
    [switch]$ShowSweepOutput
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Script)) { throw "script not found: $Script" }

$argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $Script) + $ScriptArgs
$quoted = ($argList | ForEach-Object {
    if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
}) -join ' '

Write-Host ("[sw-run] " + $Script + "  timeout=" + $TimeoutSec + "s") -ForegroundColor Cyan

# Start-Process -PassThru does NOT populate ExitCode on this box (verified:
# empty even after Refresh() and WaitForExit()). ProcessStartInfo with
# UseShellExecute=false does report it.
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'powershell.exe'
$psi.Arguments = $quoted
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $false
$proc = [System.Diagnostics.Process]::Start($psi)

$finished = $proc.WaitForExit($TimeoutSec * 1000)
$exitCode = 0

if (-not $finished) {
    Write-Host ("[sw-run] TIMEOUT after " + $TimeoutSec + "s - killing child tree") -ForegroundColor Yellow
    & taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-Null
    $exitCode = 124
} else {
    $exitCode = $proc.ExitCode
    Write-Host ("[sw-run] child exited with " + $exitCode) -ForegroundColor Cyan
}

if (-not $NoSweep) {
    Write-Host "[sw-run] sweeping orphaned hidden instances..." -ForegroundColor Cyan
    $sweep = Join-Path $PSScriptRoot 'sw-sweep.ps1'
    if (Test-Path -LiteralPath $sweep) {
        $sweepArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $sweep, '-Apply')
        if (-not $ShowSweepOutput) { $sweepArgs += '-Quiet' }
        & powershell.exe @sweepArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[sw-run] sweep reported a problem (exit $LASTEXITCODE) - check manually" -ForegroundColor Red
        }
    } else {
        Write-Host "[sw-run] sw-sweep.ps1 not found next to this script" -ForegroundColor Red
    }
}

exit $exitCode
