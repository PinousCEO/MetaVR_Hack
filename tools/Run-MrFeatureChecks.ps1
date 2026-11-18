# Regression of the MR-side features through the desktop MR simulator (scripted hands): virtual rain + shelter, hand ride,
# timer by natural language, and the two-pinch pointing selection. Results are simulator results - never Quest evidence.
# Needs: tools\Start-Dev.ps1 -Sim ... running, partner in MR.
param([string]$Out = "artifacts\mr-feature-checks.txt", [string]$Monitor = "DISPLAY3")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"

function Mr { $l = (& $ctl status | Select-String "^\s*mr:"); if ($l) { $l.Line.Trim() } else { "mr: (no MR client)" } }
function Head { (& $ctl status | Select-Object -First 1) }
function Timer { $l = (& $ctl status | Select-String "module .* timer" | Select-Object -Last 1); if ($l) { $l.Line.Trim() } else { "(no timer)" } }
function Hand([string]$mode) { & $ctl debug simHand 0 0 0 $mode | Out-Null }

$res = @("MR feature checks $(Get-Date -Format s)  (MR client: desktop simulator with scripted hands - NOT a Quest)")
# the MR client needs a few seconds to start and connect: commands relayed before that would be lost
for ($i = 0; $i -lt 30 -and (Mr) -match "no MR client"; $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 2
$res += "start: " + (Head)

$res += ""
$res += "--- virtual rain / shelter (shelter is decided by RainShelter geometry, not by a timer)"
& $ctl debug rain 1 | Out-Null; Start-Sleep -Seconds 3      # relayed to the MR client, which owns the rain setting
$r1 = Mr; $res += "R1 rain on, no hand            (expect exposed):   $r1"
Hand "beside"; Start-Sleep -Seconds 2
$r2 = Mr; $res += "R2 hand beside the head        (expect exposed):   $r2"
Hand "umbrella"; Start-Sleep -Seconds 2
$r3 = Mr; $res += "R3 palm up-wind above the head (expect sheltered): $r3"
Hand "none"; Start-Sleep -Seconds 2
$r4 = Mr; $res += "R4 hand removed                (expect exposed):   $r4"
& $ctl debug rain 0 | Out-Null; Start-Sleep -Seconds 1
$rainOk = ($r1 -match "rain:exposed") -and ($r2 -match "rain:exposed") -and ($r3 -match "rain:sheltered") -and ($r4 -match "rain:exposed")
$res += "rain/shelter: " + $(if ($rainOk) { "PASS" } else { "FAIL" })

$res += ""
$res += "--- hand ride"
Hand "offer"; Start-Sleep -Seconds 7
$h1 = Mr; $res += "H1 palm offered for 7 s        (expect onHand=True):  $h1"
Hand "carry"; Start-Sleep -Seconds 3
$h2 = Mr; $res += "H2 carried around              (expect onHand=True):  $h2"
Hand "tilt"; Start-Sleep -Seconds 3
$h3 = Mr; $res += "H3 hand tipped over            (expect onHand=False): $h3"
Hand "none"; Start-Sleep -Seconds 2
$rideOk = ($h1 -match "onHand=True") -and ($h2 -match "onHand=True") -and ($h3 -match "onHand=False")
$res += "hand ride: " + $(if ($rideOk) { "PASS" } else { "FAIL" })

$res += ""
$res += "--- timer module by natural language (panel = pooled TimerPanel object in the hierarchy)"
if ((Timer) -eq "(no timer)") { & $ctl utter "타이머 하나 만들어줘" | Out-Null; Start-Sleep -Seconds 2; $res += "T0 no timer module stored yet, created one: " + (Timer) }
& $ctl utter "타이머 3분 시작해 줘" | Out-Null; Start-Sleep -Seconds 2
$t1 = Timer; $res += "T1 after start:  $t1"
& $ctl utter "타이머 일시정지" | Out-Null; Start-Sleep -Seconds 2
$t2 = Timer; $res += "T2 after pause:  $t2"
& $ctl utter "타이머 다시 시작해" | Out-Null; Start-Sleep -Seconds 2
$t3 = Timer; $res += "T3 after resume: $t3"
$timerOk = ($t1 -match "status=running") -and ($t2 -match "status=paused") -and ($t3 -match "status=running")
$res += "timer: " + $(if ($timerOk) { "PASS" } else { "FAIL" })

$res += ""
$res += "--- pointing selection: point, pinch (preview only), pinch again on the marker (go)"
$before = Head
& $ctl debug simPoint 9 0.30 0.40 $Monitor | Out-Null; Start-Sleep -Seconds 1
& $ctl debug simPinch | Out-Null; Start-Sleep -Seconds 2
$p1 = Head; $res += "P1 after the first pinch  (expect no new transition): $p1"
& $ctl debug simPinch | Out-Null; Start-Sleep -Seconds 2
$p2 = Head; $res += "P2 after the second pinch (expect a transition running): $p2"
& $ctl wait 40 | Out-Null
$p3 = Head; $res += "P3 after waiting: $p3"
$id0 = [regex]::Match($before, "id=(\S+)").Groups[1].Value; $id1 = [regex]::Match($p1, "id=(\S+)").Groups[1].Value; $id2 = [regex]::Match($p2, "id=(\S+)").Groups[1].Value
$pointOk = ($id1 -eq $id0) -and ($id2 -ne $id0) -and ($p3 -match "owner=pc") -and ($p3 -match "transition=Complete")
$res += "pointing selection: " + $(if ($pointOk) { "PASS" } else { "FAIL" })
& $ctl return 0 0 | Out-Null
& $ctl wait 40 | Out-Null
$res += "back: " + (Head)

$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res | Select-Object -Last 40
if ($rainOk -and $rideOk -and $timerOk -and $pointOk) { exit 0 } else { exit 4 }
