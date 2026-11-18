# Task-state honesty checks with the LABELLED TEST DOUBLE harness (no harness usage, nothing here is real analysis):
#   - the workspace status indicator follows the real task events,
#   - a cancelled task ends as TaskCancelled,
#   - a change that produced no file change is reported as FAILED, never as success.
# Requires: tools\Start-Dev.ps1 -Sim -StubHarness and the sample window (tools\Setup-Sample.ps1 -Open).
param([string]$Title = "Momo Notes", [string]$Out = "artifacts\feature-tests.txt")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$hostLog = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\host.log"
function Top { (& $ctl status | Select-Object -First 1) }
function Shot($name) {
    $pc = (& $ctl status | Select-String "pc:").Line; $m = [regex]::Match($pc, "feetPx=\((-?\d+),(-?\d+)\)")
    if ($m.Success) { powershell -NoProfile -ExecutionPolicy Bypass -File tools\Capture-Screen.ps1 -X ([int]$m.Groups[1].Value - 200) -Y ([int]$m.Groups[2].Value - 430) -W 460 -H 460 -Out "artifacts\$name.png" -Scale 0.6 | Out-Null }
}
powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action restore -Title $Title | Out-Null
$r = powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action find -Title $Title
$m = [regex]::Match($r, "rect=\[(-?\d+),(-?\d+) (\d+)x(\d+)\]")
$px = [int]$m.Groups[1].Value + [int]([int]$m.Groups[3].Value * 0.5); $py = [int]$m.Groups[2].Value + [int]([int]$m.Groups[4].Value * 0.5)
& $ctl owner mr | Out-Null; Start-Sleep -Seconds 2
& $ctl enter $px $py | Out-Null; & $ctl wait 40 | Out-Null; Start-Sleep -Seconds 1
$res = @("", "--- task-state honesty checks with the LABELLED TEST DOUBLE harness $(Get-Date -Format s)")
$n = (Get-Content $hostLog).Count
# 1. status indicator while a task is really running
& $ctl task analyze | Out-Null; Start-Sleep -Milliseconds 1300
$res += "K1 during analysis: " + (Top); Shot "status-analyzing"
& $ctl waittask "ProposalReady|TaskFailed" 20 | Out-Null; Start-Sleep -Milliseconds 500
$res += "K2 after analysis: " + (Top); Shot "status-done"
# 2. cancel
& $ctl task analyze | Out-Null; Start-Sleep -Milliseconds 900; & $ctl task cancel | Out-Null
& $ctl waittask "TaskCancelled|ProposalReady|TaskFailed" 20 | Out-Null; Start-Sleep -Milliseconds 400
$res += "K3 analysis cancelled by the user: " + ((& $ctl tasks | Select-String "^task " | Select-Object -Last 1).Line)
# 3. a change that changes nothing must fail
& $ctl debug select 0 | Out-Null
& $ctl waittask "TaskFailed|ReviewRequired|TestFailed" 60 | Out-Null; Start-Sleep -Milliseconds 500
$res += "K4 change where the harness edited nothing: " + ((& $ctl tasks | Select-String "^task .*\[change\]" | Select-Object -Last 1).Line)
$res += "   " + (Top); Shot "status-error"
$res += (Get-Content $hostLog | Select-Object -Skip $n | Select-String "task task-|say\[" | ForEach-Object { "      " + $_.Line.Substring(11, [Math]::Min(170, $_.Line.Length - 11)) })
& $ctl task clearActivity | Out-Null
& $ctl return 0 0 | Out-Null; & $ctl wait 40 | Out-Null
$res | Add-Content (Join-Path $root $Out) -Encoding utf8
$res
