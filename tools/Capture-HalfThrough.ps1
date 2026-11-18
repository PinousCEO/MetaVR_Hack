# Freezes a crossing with the body half-way through the display (debug freeze at 50 %) in each direction and captures
# both renderers at that moment: the real desktop around the portal (what the PC draws) and the simulator's own view
# (what the MR side draws). The two images together show the complementary halves. Simulator MR side - not a Quest.
# Needs: tools\Start-Dev.ps1 -Sim ... running, partner in MR.
param([int]$X = 1350, [int]$Y = -420, [double]$Progress = 0.5, [string]$Prefix = "artifacts\half-through")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
function Head { (& $ctl status | Select-Object -First 1) }
function Shots([string]$name) {
    & (Join-Path $PSScriptRoot "Capture-Screen.ps1") -X ($X - 360) -Y ($Y - 400) -W 720 -H 760 -Out "$Prefix-$name-pc.png" | Out-Null
    $mr = Join-Path $root "$Prefix-$name-mr.png"
    & $ctl debug simShot 0 0 0 $mr | Out-Null
    Start-Sleep -Milliseconds 900
    "$name captured: $Prefix-$name-pc.png (real desktop), $Prefix-$name-mr.png (simulator view)  state: " + (Head)
}

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 3
$res = @("Half-through captures $(Get-Date -Format s)  (portal at pixel $X,$Y; frozen at $([int]($Progress * 100)) % of the crossing; MR side = desktop simulator)")
$res += "start: " + (Head)

& $ctl freeze $Progress | Out-Null
& $ctl enter $X $Y | Out-Null
Start-Sleep -Seconds 10
$res += Shots "enter"
& $ctl play | Out-Null
& $ctl wait 40 | Out-Null
$res += "after resuming the entry: " + (Head)
Start-Sleep -Seconds 2

& $ctl freeze $Progress | Out-Null
& $ctl return $X $Y | Out-Null
Start-Sleep -Seconds 10
$res += Shots "exit"
& $ctl play | Out-Null
& $ctl wait 40 | Out-Null
$res += "after resuming the exit: " + (Head)
$res | Set-Content -Path (Join-Path $root "$Prefix.txt") -Encoding utf8
$res
