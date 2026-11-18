# Records one unedited MR -> PC -> MR round trip: a single continuous screen capture of a strip of the real desktop
# that contains the desktop MR simulator window (top) and the real target window with the transparent PC character
# (below). No cuts and no video speed change. With -Slow the crossing itself is run slower by the host's debug clock
# (both renderers follow the same slowed clock), which must be stated wherever such a clip is shown.
# The MR side is the SIMULATOR, not a Quest.
# Needs: tools\Start-Dev.ps1 -Sim ... running, partner in MR, ffmpeg on PATH.
param(
    [int]$X = -490, [int]$Y = -800, [int]$W = 480, [int]$H = 1100,      # capture rectangle (virtual-desktop pixels)
    [int]$EnterX = -250, [int]$EnterY = -60,                             # portal position for the entry
    [string]$Out = "artifacts\portal-roundtrip-final-build.mp4",
    [double]$Slow = 0,                                                   # e.g. 0.25 = quarter speed (debug slow motion, stated in the file name)
    [switch]$EntryOnly,                                                  # record only MR -> PC (the partner is brought back afterwards, unrecorded)
    [int]$Seconds = 34
)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$outPath = Join-Path $root $Out
if (Test-Path $outPath) { Remove-Item $outPath -Force }

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 3
$ffArgs = "-y -loglevel error -f gdigrab -framerate 30 -offset_x $X -offset_y $Y -video_size ${W}x${H} -i desktop -t $Seconds -c:v libx264 -preset veryfast -pix_fmt yuv420p -crf 22 `"$outPath`""
$rec = Start-Process -FilePath $ffmpeg -ArgumentList $ffArgs -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
"before: " + (& $ctl status | Select-Object -First 1)
if ($Slow -gt 0) { & $ctl slow $Slow | Out-Null; "crossing speed set to $Slow x (debug slow motion)" }
& $ctl enter $EnterX $EnterY | Out-Null
& $ctl wait 90 | Out-Null
"after entering: " + (& $ctl status | Select-Object -First 1)
if (-not $EntryOnly)
{
    Start-Sleep -Seconds 3
    & $ctl return 0 0 | Out-Null
    & $ctl wait 90 | Out-Null
    "after returning: " + (& $ctl status | Select-Object -First 1)
}
$rec.WaitForExit(($Seconds + 30) * 1000) | Out-Null
if ($Slow -gt 0) { & $ctl slow 1 | Out-Null }
if ($EntryOnly) { & $ctl return 0 0 | Out-Null; & $ctl wait 60 | Out-Null; "brought back (not recorded): " + (& $ctl status | Select-Object -First 1) }
if (Test-Path $outPath) { "recorded: $Out  " + [math]::Round((Get-Item $outPath).Length / 1MB, 2) + " MB" } else { "RECORDING FAILED" }
