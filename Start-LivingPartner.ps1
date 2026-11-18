# Living AI Partner — one-step launcher for the PC side.
# Starts the Companion Host (tray icon), which starts the Windows character client. Run from the repository root
# or from the dist folder. The Quest app is started on the headset; see RUNBOOK.md for pairing and calibration.
param(
    [switch]$LoopbackOnly,   # do not listen on the LAN (a headset cannot connect in this mode)
    [string]$Monitor = ""    # optional start display for the PC character, e.g. DISPLAY2
)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidates = @(
    (Join-Path $here "dist\LivingAIPartner-Host\LivingPartner.Host.exe"),
    (Join-Path $here "LivingAIPartner-Host\LivingPartner.Host.exe"),
    (Join-Path $here "Companion\LivingPartner.Host\bin\Release\net8.0-windows\LivingPartner.Host.exe")
)
$hostExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $hostExe) { Write-Host "Companion Host not found. Build it first (see RUNBOOK.md)." -ForegroundColor Red; exit 1 }
$clients = @(
    (Join-Path $here "dist\LivingAIPartner-Windows\LivingAIPartner.exe"),
    (Join-Path $here "LivingAIPartner-Windows\LivingAIPartner.exe")
)
$client = $clients | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $client) { Write-Host "Windows character client not found. Build it first (see RUNBOOK.md)." -ForegroundColor Red; exit 1 }
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
    $apk = Join-Path (Split-Path (Split-Path $client -Parent) -Parent) "LivingAIPartner-Quest\LivingAIPartner.apk"
    if (Test-Path -LiteralPath $apk) {
        $package = "com.livingaipartner.quest"
        $remote = (& $adb -s $serial shell pm path $package 2>$null) | Where-Object { $_ -match '^package:/.*/base\.apk$' } | Select-Object -First 1
        $installed = if ($remote) { ((& $adb -s $serial shell sha256sum ($remote.Substring(8).Trim()) 2>$null) -split '\s+')[0] }
        if ($installed -ne (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash) {
            if (& $adb -s $serial shell pidof $package 2>$null) {
                Write-Host "A new Quest build is available. Close the game and run this launcher to install it."
            } else {
                & $adb -s $serial install -r $apk
                if ($LASTEXITCODE -ne 0) { Write-Warning "Quest installation failed. The companion is running." }
            }
        }
    }
} else {
    Start-Process -FilePath $hostExe -ArgumentList $a
    Write-Host "The Bambi Companion window shows the Quest connection or pairing code."
}
if (-not $LoopbackOnly) { Write-Host "The first run asks Windows Firewall for permission on private networks: allow it so the headset can connect." }
