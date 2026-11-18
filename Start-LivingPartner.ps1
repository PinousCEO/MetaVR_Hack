# Yonder — one-step launcher for the PC side.
# Starts the Companion Host (tray icon), which starts the Windows character client. Run from the repository root
# or from the dist folder. The Quest app is started on the headset; see RUNBOOK.md for pairing and calibration.
param(
    [switch]$LoopbackOnly,   # do not listen on the LAN (a headset cannot connect in this mode)
    [string]$Monitor = ""    # optional start display for the PC character, e.g. DISPLAY2
)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidates = @(
    (Join-Path $here "dist\Yonder\LivingAIPartner-Host\LivingPartner.Host.exe"),
    (Join-Path $here "dist\LivingAIPartner-Host\LivingPartner.Host.exe"),
    (Join-Path $here "LivingAIPartner-Host\LivingPartner.Host.exe"),
    (Join-Path $here "Companion\LivingPartner.Host\bin\Release\net8.0-windows\LivingPartner.Host.exe")
)
$hostExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $hostExe) { Write-Host "Companion Host not found. Build it first (see RUNBOOK.md)." -ForegroundColor Red; exit 1 }
$clients = @(
    (Join-Path $here "dist\Yonder\LivingAIPartner-Windows\LivingAIPartner.exe"),
    (Join-Path $here "dist\LivingAIPartner-Windows\LivingAIPartner.exe"),
    (Join-Path $here "LivingAIPartner-Windows\LivingAIPartner.exe")
)
$client = $clients | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $client) { Write-Host "Windows character client not found. Build it first (see RUNBOOK.md)." -ForegroundColor Red; exit 1 }
$currentHost = Get-Process -Name "LivingPartner.Host" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($currentHost) {
    if ($currentHost.Path -eq $hostExe) { Write-Host "Yonder is already running."; exit 0 }
    Write-Host "An earlier companion is running. Quit it from its tray icon, then open Start-Yonder again." -ForegroundColor Yellow
    exit 2
}
$adb = (Get-Command adb.exe -ErrorAction SilentlyContinue).Source
if (!$adb) {
    $adb = Get-ChildItem "$env:ProgramFiles\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1 -ExpandProperty FullName
}
$usb = $false
if ($adb) {
    $serial = & $adb -d get-serialno 2>$null
    if ($LASTEXITCODE -eq 0 -and $serial -and ((& $adb -s $serial shell getprop ro.product.model 2>$null) -match 'Quest')) {
        & $adb -s $serial reverse tcp:47610 tcp:47610 | Out-Null
        $usb = $LASTEXITCODE -eq 0
    }
}
$a = @("--client", "`"$client`"")
if ($LoopbackOnly) { $a += "--loopback-only" }
if ($usb) { $a += "--usb-dev" }
if ($Monitor) { $a += @("--client-args", "`"-lp-monitor $Monitor`"") }
if ($usb) {
    Start-Process -FilePath $hostExe -ArgumentList $a -WindowStyle Hidden
    Write-Host "USB forwarding is ready. The headset app connects automatically."
} else {
    Start-Process -FilePath $hostExe -ArgumentList $a -WindowStyle Hidden
    Write-Host "The Yonder Companion window shows the Quest connection or pairing code."
}
if (-not $LoopbackOnly) { Write-Host "The first run asks Windows Firewall for permission on private networks: allow it so the headset can connect." }
