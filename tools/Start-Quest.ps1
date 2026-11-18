# Runs the Quest app against this PC over the USB cable (development): installs the APK if asked, forwards the host's
# port to the headset (adb reverse), starts the host with --usb-dev (a headset on the cable is accepted without the
# pairing code) if it is not running, waits until the headset is being WORN - the system refuses to start apps from the
# lock screen - then starts the app and reports the link. It then follows the app's log for -Watch seconds.
#   .\tools\Start-Quest.ps1 -Install        # after a new build
#   .\tools\Start-Quest.ps1                 # start it again
# Needs: the headset in developer mode on a USB data cable, "allow USB debugging" accepted in the headset.
param([switch]$Install, [int]$WaitWorn = 600, [int]$Watch = 0, [string]$Out = "")
$root = Split-Path -Parent $PSScriptRoot
$dist = if (Test-Path (Join-Path $root 'dist\LivingAIPartner-Host')) { Join-Path $root 'dist' } else { $root }
Set-Location $root
$pkg = "com.livingaipartner.quest"; $port = 47610
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) { $adb = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1 -ExpandProperty FullName }
if (-not $adb) { Write-Output "adb not found (install the Android platform-tools or Unity's Android module)"; exit 1 }
function Say($s) { $line = "{0:HH:mm:ss} {1}" -f (Get-Date), $s; Write-Output $line; if ($Out) { Add-Content -Path $Out -Value $line -Encoding utf8 } }
function Focus { $f = (& $adb shell dumpsys window | Select-String "mCurrentFocus=Window") | Select-Object -Last 1; if ($f) { ($f.Line -replace ".*u0 ", "" -replace "\}.*", "").Trim() } else { "none" } }
function Awake { ((& $adb shell dumpsys power | Select-String "mWakefulness=") -join "") -match "Awake" }
function AppLog([int]$n) { & $adb logcat -d -s Unity:V 2>$null | Select-String "\[Quest\]|\[MR\]|Exception" | Select-Object -Last $n | ForEach-Object { ($_.Line -replace "^.*?Unity\s+:\s", "") } }

$dev = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice\s*$" })
if ($dev.Count -eq 0) { Say "no headset on adb: check the cable, wake the headset, accept 'allow USB debugging' inside it"; & $adb devices; exit 2 }
Say ("headset: " + (& $adb shell getprop ro.product.model) + ", battery " + (((& $adb shell dumpsys battery | Select-String "level") -join "").Trim()))
if ($Install) {
    $apk = Join-Path $dist "LivingAIPartner-Quest\LivingAIPartner.apk"
    & $adb shell am force-stop $pkg
    Say ("install " + (Get-Item $apk).LastWriteTime + ": " + ((& $adb install -r $apk 2>&1 | Select-Object -Last 1)))
    # what the app needs is granted here, so that no permission dialog has to be answered inside the headset
    foreach ($perm in "horizonos.permission.HEADSET_CAMERA", "android.permission.CAMERA", "com.oculus.permission.USE_SCENE", "android.permission.RECORD_AUDIO") { & $adb shell pm grant $pkg $perm 2>$null | Out-Null }
}
& $adb reverse tcp:$port tcp:$port | Out-Null

# The solo game also uses the host executable, on its own ephemeral port. Only the companion answers this endpoint.
$companion = try { Invoke-RestMethod -Uri "http://127.0.0.1:$port/status" -TimeoutSec 2 -ErrorAction Stop } catch { $null }
if ($null -eq $companion -or $null -eq $companion.PSObject.Properties["clients"]) {
    $hostExe = Join-Path $dist "LivingAIPartner-Host\LivingPartner.Host.exe"
    if (-not (Test-Path $hostExe)) { $hostExe = Join-Path $root "Companion\LivingPartner.Host\bin\Release\net8.0-windows\LivingPartner.Host.exe" }
    $client = Join-Path $dist "LivingAIPartner-Windows\LivingAIPartner.exe"
    Start-Process -FilePath $hostExe -ArgumentList "--client", "`"$client`"", "--loopback-only", "--usb-dev" | Out-Null
    Start-Sleep -Seconds 6
    Say "host started (this PC only, --usb-dev)"
} else { Say "host already running (it must have been started with --usb-dev for the cable connection to be accepted without pairing)" }

Say "put the headset on now - waiting up to $WaitWorn s"
$sw = [Diagnostics.Stopwatch]::StartNew(); $worn = $false
while ($sw.Elapsed.TotalSeconds -lt $WaitWorn) {
    $f = Focus
    if ((Awake) -and $f -ne "none" -and $f -notmatch "vrlockscreen|voidactivity") { $worn = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $worn) { Say "the headset was not put on in time"; exit 3 }
Start-Sleep -Seconds 2
& $adb logcat -c
& $adb shell am start -n "$pkg/com.unity3d.player.UnityPlayerGameActivity" | Out-Null
Say "app started on the headset"
Start-Sleep -Seconds 14
$ctl = Join-Path $dist "LivingAIPartner-Host\lpctl.exe"
Say ("app process: " + $(if (& $adb shell pidof $pkg) { "running" } else { "NOT running" }))
AppLog 6 | ForEach-Object { Say ("  " + $_.Substring(0, [Math]::Min(260, $_.Length))) }
(& $ctl status | Select-String "^clients|mr:") | ForEach-Object { Say ("  host " + $_.Line.Trim()) }

# follow what happens for a while: connection, hands, calibration, crossings
$last = ""; $sw.Restart()
while ($sw.Elapsed.TotalSeconds -lt $Watch) {
    Start-Sleep -Seconds 5
    $st = (& $ctl status) -join "`n"
    $mr = if ($st -match "(?m)^\s*mr: (.*)$") { $Matches[1] } else { "not connected" }
    $head = ($st -split "`n")[0]
    $cal = if ($st -match "calibrations=(\d+)") { $Matches[1] } else { "?" }
    $tn = if ($st -match "(?m)^\s*tennis: (phase=\w+ user=\d+ partner=\d+ rally=\d+)") { " | tennis " + $Matches[1] } else { "" }
    $now = "$head | calibrations=$cal | mr: $mr$tn" -replace "fps=\d+", "" -replace "id=\S+", ""
    if ($now -ne $last) { $last = $now; Say $now.Substring(0, [Math]::Min(300, $now.Length)) }
    if (-not (& $adb shell pidof $pkg)) { Say "the app is no longer running on the headset (closed, or another app took over)"; break }
}
if ($Watch -gt 0) { Say "--- last lines of the app's log"; AppLog 14 | ForEach-Object { Say ("  " + $_.Substring(0, [Math]::Min(280, $_.Length))) } }
