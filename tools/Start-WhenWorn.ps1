# Waits (hidden, for up to -Hours) until the Quest on the cable is being WORN, and only then starts the host and the app
# through Start-Quest.ps1 - so that nothing appears on the PC before the user puts the headset on.
#   Start-Process powershell -WindowStyle Hidden -ArgumentList "-ExecutionPolicy Bypass -File tools\Start-WhenWorn.ps1"
# What happened is written to artifacts\quest-autostart.txt.
param([double]$Hours = 6)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$out = Join-Path $root "artifacts\quest-autostart.txt"
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) { $adb = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1 -ExpandProperty FullName }
if (-not $adb) { Set-Content $out ("{0:HH:mm:ss} adb not found" -f (Get-Date)) -Encoding utf8; exit 1 }
Set-Content $out ("{0:HH:mm:ss} waiting until the headset is worn (up to $Hours h); nothing is started before that" -f (Get-Date)) -Encoding utf8
$until = (Get-Date).AddHours($Hours)
$installed = $false
while ((Get-Date) -lt $until) {
    $dev = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\sdevice\s*$" })
    if ($dev.Count -gt 0) {
        # the newest APK first, once (the headset may only now have allowed USB debugging - "unauthorized" until then)
        if (-not $installed) {
            $apk = Join-Path $root "dist\LivingAIPartner-Quest\LivingAIPartner.apk"
            $pkg = "com.livingaipartner.quest"
            & $adb shell am force-stop $pkg
            $res = (& $adb install -r $apk 2>&1 | Select-Object -Last 1)
            Add-Content $out ("{0:HH:mm:ss} install of the build of {1:HH:mm}: {2}" -f (Get-Date), (Get-Item $apk).LastWriteTime, $res) -Encoding utf8
            if ("$res" -match "Success") {
                foreach ($perm in "horizonos.permission.HEADSET_CAMERA", "android.permission.CAMERA", "com.oculus.permission.USE_SCENE", "android.permission.RECORD_AUDIO") { & $adb shell pm grant $pkg $perm 2>$null | Out-Null }
                $installed = $true
            } else { Start-Sleep -Seconds 6; continue }
        }
        $awake = ((& $adb shell dumpsys power | Select-String "mWakefulness=") -join "") -match "Awake"
        $f = (& $adb shell dumpsys window | Select-String "mCurrentFocus=Window") | Select-Object -Last 1
        $focus = if ($f) { $f.Line } else { "none" }
        if ($awake -and $focus -ne "none" -and $focus -notmatch "vrlockscreen|voidactivity") {
            Add-Content $out ("{0:HH:mm:ss} the headset is worn: starting" -f (Get-Date)) -Encoding utf8
            & (Join-Path $PSScriptRoot "Start-Quest.ps1") -WaitWorn 120 -Out $out
            exit 0
        }
    }
    Start-Sleep -Seconds 4
}
Add-Content $out ("{0:HH:mm:ss} gave up: the headset was not worn" -f (Get-Date)) -Encoding utf8
