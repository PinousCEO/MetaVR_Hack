# Drives the REAL monitor-calibration flow (four lit corners + desk) with the simulator's scripted fingertip, and records
# where the PC puts its native corner markers. Verifies the flow logic and the markers - not hand-tracking accuracy.
# Requires tools\Start-Dev.ps1 -Sim. Displays listed in -Skip are skipped (e.g. one that is showing a full-screen app).
param([string[]]$Skip = @("DISPLAY2"), [string]$Out = "artifacts\calibration-flow.txt")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$hostLog = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\host.log"
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function F($v) { ([double]$v).ToString("0.#####", $inv) }
$snap = (& $ctl snapshot) -join "`n" | ConvertFrom-Json
$n = (Get-Content $hostLog).Count
$res = @("Calibration flow $(Get-Date -Format s) (scripted simulator fingertip; calibrations are flagged simulated)")
& $ctl debug calibrate 1 | Out-Null
Start-Sleep -Milliseconds 1500
foreach ($m in ($snap.monitors | Sort-Object id)) {
    $short = $m.id -replace '\\\\\.\\', ''
    if ($Skip -contains $short) { & $ctl debug calibrateSkip | Out-Null; $res += "$short skipped"; Start-Sleep -Milliseconds 1200; continue }
    $c = $snap.calibrations | Where-Object { $_.monitorId -eq $m.id -and $_.simulated } | Select-Object -First 1
    if (-not $c) { & $ctl debug calibrateSkip | Out-Null; $res += "$short has no simulator layout; skipped"; Start-Sleep -Milliseconds 1200; continue }
    Start-Sleep -Milliseconds 800
    # where did the PC put its corner markers? (native windows titled LivingPartnerCalib)
    $markers = powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action zorder -Title "LivingPartnerCalib" | Select-String "LivingPartnerCalib" | ForEach-Object { ([regex]::Match($_.Line, "rect=\[[^\]]+\]")).Value }
    $res += "$short bounds=[$($m.bounds.x),$($m.bounds.y) $($m.bounds.w)x$($m.bounds.h)] corner markers: " + ($markers -join " ")
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\Capture-Screen.ps1 -X $m.bounds.x -Y $m.bounds.y -W 120 -H 120 -Out "artifacts\calib-marker-$short-topleft.png" | Out-Null
    foreach ($corner in @($c.tl, $c.tr, $c.bl, $c.br)) {
        & $ctl debug simTouch (F $corner.z) (F $corner.x) (F $corner.y) | Out-Null
        Start-Sleep -Milliseconds 1500
    }
    $dx = ([double]$c.bl.x + [double]$c.br.x) / 2; $dz = ([double]$c.bl.z + [double]$c.br.z) / 2 - 0.2
    & $ctl debug simTouch (F $dz) (F $dx) (F $c.deskY) | Out-Null
    Start-Sleep -Milliseconds 1800
}
Start-Sleep -Milliseconds 800
& $ctl debug calibrateEnd | Out-Null
Start-Sleep -Milliseconds 800
$left = powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action zorder -Title "LivingPartnerCalib" | Select-String "LivingPartnerCalib"
$res += "markers left on screen after the flow ended: " + @($left).Count
$res += (Get-Content $hostLog | Select-Object -Skip $n | Select-String "calibration" | ForEach-Object { "   " + $_.Line.Substring(11) })
$res | Set-Content (Join-Path $root $Out) -Encoding utf8
$res
