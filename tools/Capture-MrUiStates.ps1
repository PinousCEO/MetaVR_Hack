# Takes screenshots of the MR-side UI states from the desktop MR simulator (its own in-app screenshot, so nothing on the
# desktop is captured): rain + shelter, pointing marker, calibration guide, caption. Simulator images - not a Quest.
# Needs: tools\Start-Dev.ps1 -Sim ... running, partner in MR.
param([string]$Monitor = "DISPLAY3", [string]$OutDir = "artifacts")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
function Shot([string]$name) { $p = Join-Path (Join-Path $root $OutDir) $name; & $ctl debug simShot 0 0 0 $p | Out-Null; Start-Sleep -Milliseconds 900; if (Test-Path $p) { "saved $OutDir\$name" } else { "MISSING $OutDir\$name" } }

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 3

& $ctl debug rain 1 | Out-Null; Start-Sleep -Seconds 2
Shot "sim-ui-rain-exposed.png"
& $ctl debug simHand 0 0 0 umbrella | Out-Null; Start-Sleep -Seconds 2
Shot "sim-ui-rain-sheltered.png"
& $ctl debug simHand 0 0 0 none | Out-Null
& $ctl debug rain 0 | Out-Null; Start-Sleep -Seconds 1

& $ctl debug simPoint 7 0.50 0.45 $Monitor | Out-Null; Start-Sleep -Seconds 1
& $ctl debug simPinch | Out-Null; Start-Sleep -Milliseconds 1500
Shot "sim-ui-pointing-marker.png"
Start-Sleep -Seconds 8          # let the unconfirmed selection expire: nothing may start

& $ctl debug calibrate 1 | Out-Null; Start-Sleep -Seconds 2
Shot "sim-ui-calibration-panel.png"
& $ctl debug calibrateEnd | Out-Null; Start-Sleep -Seconds 1

& $ctl debug say 0 0 0 "Hello! I am Momo. Point at a screen and pinch." | Out-Null; Start-Sleep -Milliseconds 1200
Shot "sim-ui-caption.png"
"state after the captures: " + (& $ctl status | Select-Object -First 1)
