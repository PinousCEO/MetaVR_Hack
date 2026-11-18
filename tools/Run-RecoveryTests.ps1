# Failure-path checks for the portal (Gate 3 / R10-R12): window moved or minimised while the partner is on its way,
# MR client lost before and during the crossing, PC client lost during the crossing. Requires tools\Start-Dev.ps1 -Sim
# and the sample window ("Momo Notes") to be open. Results are appended to artifacts\recovery-tests.txt.
param([string]$Title = "Momo Notes", [string]$Out = "artifacts\recovery-tests.txt")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$client = Join-Path $root "dist\LivingAIPartner-Windows\LivingAIPartner.exe"
$hostLog = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\host.log"
function W($action, $extra = "") { Invoke-Expression "powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action $action -Title '$Title' $extra" | Out-Null }
function State { (& $ctl status | Select-Object -First 1) }
function Rect { $r = powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action find -Title $Title; $m = [regex]::Match($r, "rect=\[(-?\d+),(-?\d+) (\d+)x(\d+)\]"); return @([int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value, [int]$m.Groups[4].Value) }
function LogSince($n) { Get-Content $hostLog | Select-Object -Skip $n | Select-String "transition tr-|finished|client (dis)?connected: role=(sim|pc)|character client" | ForEach-Object { "      " + $_.Line.Substring(11) } }
function StartSim { Start-Process -FilePath $client -ArgumentList "-lp-role mrsim -lp-sim-x -500 -lp-sim-y -790 -lp-sim-w 490 -lp-sim-h 420 -screen-fullscreen 0 -screen-width 490 -screen-height 420" | Out-Null }
function SimProc { Get-CimInstance Win32_Process -Filter "Name='LivingAIPartner.exe'" | Where-Object { $_.CommandLine -match "mrsim" } }
function PcProc { Get-CimInstance Win32_Process -Filter "Name='LivingAIPartner.exe'" | Where-Object { $_.CommandLine -match "-lp-role pc" } }
function GoHome { & $ctl play | Out-Null; if ((State) -match "owner=pc") { & $ctl return 0 0 | Out-Null; & $ctl wait 30 | Out-Null }; Start-Sleep -Milliseconds 600 }
$res = @("Recovery tests $(Get-Date -Format s)")

# --- A. window moves while the partner is approaching: the window-anchored portal follows it
GoHome; W restore; W move "-X 300 -Y -950 -W 1000 -H 700"; Start-Sleep -Milliseconds 800
$r = Rect; $n = (Get-Content $hostLog).Count
& $ctl enter ($r[0] + 400) ($r[1] + 300) | Out-Null; Start-Sleep -Milliseconds 1500
W move "-X 620 -Y -880"
& $ctl wait 40 | Out-Null
$res += "A. window moved during approach -> $(State)"; $res += LogSince $n

# --- B. window minimised while approaching: hold, then continue after restore
GoHome; $r = Rect; $n = (Get-Content $hostLog).Count
& $ctl enter ($r[0] + 400) ($r[1] + 300) | Out-Null; Start-Sleep -Milliseconds 1200
W minimize; Start-Sleep -Seconds 4
$res += "B1. minimised during approach -> $(State)"
W restore
& $ctl wait 40 | Out-Null
$res += "B2. after restore -> $(State)"; $res += LogSince $n

# --- C. MR client lost while approaching, then comes back
GoHome; $r = Rect; $n = (Get-Content $hostLog).Count
& $ctl enter ($r[0] + 400) ($r[1] + 300) | Out-Null; Start-Sleep -Milliseconds 1500
SimProc | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 3
$res += "C1. MR client killed during approach -> $(State)"
StartSim
& $ctl wait 45 | Out-Null
$res += "C2. MR client back -> $(State)"; $res += LogSince $n

# --- D. MR client (the source) lost after the body is already in the portal: the PC finishes alone, no duplicate
GoHome; $r = Rect; $n = (Get-Content $hostLog).Count
& $ctl freeze 0.5 | Out-Null
& $ctl enter ($r[0] + 400) ($r[1] + 300) | Out-Null; & $ctl watch 9 | Out-Null
SimProc | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Milliseconds 500
& $ctl play | Out-Null
& $ctl wait 20 | Out-Null; Start-Sleep -Seconds 1
$res += "D1. MR source killed at 50% then resumed -> $(State)"
StartSim; Start-Sleep -Seconds 8
$res += "D2. MR client back -> $(State) | $((& $ctl status | Select-String 'mr:').Line.Trim())"; $res += LogSince $n

# --- E. PC client (the destination) lost during the crossing: rewind to MR, never two characters, never lost
GoHome; $r = Rect; $n = (Get-Content $hostLog).Count
& $ctl freeze 0.3 | Out-Null
& $ctl enter ($r[0] + 400) ($r[1] + 300) | Out-Null; & $ctl watch 9 | Out-Null
PcProc | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Milliseconds 500
& $ctl play | Out-Null
& $ctl wait 20 | Out-Null; Start-Sleep -Seconds 6
$res += "E. PC destination killed at 30% -> $(State) | $((& $ctl status | Select-String 'pc:').Line.Trim())"; $res += LogSince $n

$res | Add-Content -Path (Join-Path $root $Out) -Encoding utf8
$res

