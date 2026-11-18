# Waits until a Quest shows up on adb (cable in, headset awake, USB debugging allowed), then installs the newest APK
# and starts everything through Start-Quest.ps1 (which waits until the headset is being worn). Meant to be left
# running in the background after a build made while no headset was connected:
#   Start-Process powershell -WindowStyle Hidden -ArgumentList "-ExecutionPolicy Bypass -File tools\Wait-Quest.ps1"
# What happened is written to artifacts\quest-autostart.txt.
param([int]$Hours = 4)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$out = Join-Path $root "artifacts\quest-autostart.txt"
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) { $adb = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1 -ExpandProperty FullName }
if (-not $adb) { Add-Content $out ("{0:HH:mm:ss} adb not found" -f (Get-Date)); exit 1 }
Set-Content $out ("{0:HH:mm:ss} waiting for a headset on the cable (up to $Hours h)" -f (Get-Date)) -Encoding utf8
$until = (Get-Date).AddHours($Hours)
while ((Get-Date) -lt $until) {
    $dev = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice\s*$" })
    if ($dev.Count -gt 0) {
        Add-Content $out ("{0:HH:mm:ss} headset found: installing and starting" -f (Get-Date)) -Encoding utf8
        & $adb logcat -G 16M 2>$null; & $adb logcat -c 2>$null
        & (Join-Path $PSScriptRoot "Start-Quest.ps1") -Install -WaitWorn 3600 -Watch 120 -Out $out | Out-Null
        # what the app itself wrote in its first two minutes, and anything in the crash buffer, kept for reading afterwards
        # (the headset's log rolls over within minutes; the first start of an earlier build ended after 14 s and left no trace)
        & $adb logcat -d -s Unity:V 2>$null | Select-Object -Last 600 | Set-Content (Join-Path $root "artifacts\quest-unity.log") -Encoding utf8
        & $adb logcat -d -b crash 2>$null | Select-Object -Last 200 | Set-Content (Join-Path $root "artifacts\quest-crash.log") -Encoding utf8
        exit 0
    }
    Start-Sleep -Seconds 15
}
Add-Content $out ("{0:HH:mm:ss} no headset appeared" -f (Get-Date)) -Encoding utf8
