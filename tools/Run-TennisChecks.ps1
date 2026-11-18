# Tennis: the game played end to end against the real host and the real Windows character client, with the desktop MR
# simulator standing in for the headset. The simulator's scripted open hand plays the ball (simHand tennis / tennisMiss).
#   T1 start    - asked from the room: the partner goes into the screen by itself, then the game begins
#   T2 rally    - the hand returns the partner's balls: strokes by both sides are seen and a rally builds
#   T3 points   - the hand stops playing: the partner wins points, the game ends at 5 and the court closes
#   T4 stop     - a second game is stopped in the middle from the room
#   T5 after    - the partner is back on the desktop of the PC and can come out to the desk again
# What this does NOT show: a real hand on a Quest (hand tracking, the ball seen through passthrough against the real
# monitor). Needs: tools\Start-Dev.ps1 -Sim ... running.
param([string]$Out = "artifacts\tennis-checks.txt", [int]$RallySeconds = 45, [string]$ShotDir = "artifacts")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
function Status { (& $ctl status) -join "`n" }
function Tennis {
    $s = Status
    if ($s -match "tennis: phase=(\w+) user=(\d+) partner=(\d+) rally=(\d+) best=(\d+) shot=(\S+) last=(\w*) note='([^']*)' court=(\S+) on ?(\S*)") {      # (a court on the desk has no display behind "on")
        [pscustomobject]@{ phase = $Matches[1]; user = [int]$Matches[2]; partner = [int]$Matches[3]; rally = [int]$Matches[4]; best = [int]$Matches[5]; shot = $Matches[6]; last = $Matches[7]; note = $Matches[8]; court = $Matches[9]; mon = $Matches[10] }
    }
}
function PcMode { if ((Status) -match "pc: mode=(\w+)") { $Matches[1] } else { "" } }
function Hand($mode) { & $ctl debug simHand 0 0 0 $mode | Out-Null }
function WaitGame([int]$sec) { for ($i = 0; $i -lt $sec * 2; $i++) { $t = Tennis; if ($t) { return $t }; Start-Sleep -Milliseconds 500 }; $null }
function WaitNoGame([int]$sec) { for ($i = 0; $i -lt $sec * 2; $i++) { if (-not (Tennis)) { return $true }; Start-Sleep -Milliseconds 500 }; $false }

for ($i = 0; $i -lt 30 -and -not ((Status) -match "(?m)^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 3
$res = @("Tennis checks $(Get-Date -Format s)  (the user's hand is the simulator's scripted hand - NOT a tracked hand on a Quest)")
$ok = $true
$res += "start: " + ((Status) -split "`n")[0]

# ---- T1
Hand "tennis"
& $ctl tennis start | Out-Null
$t = WaitGame 45
$head = ((Status) -split "`n")[0]
$p1 = ($null -ne $t) -and ($head -match "owner=pc")
$res += ""; $res += "--- T1 start from the room (the partner goes in first)"
$res += "game: $(if ($t) { "phase=$($t.phase) court=$($t.court) on $($t.mon)" } else { 'none after 45 s' })  | $head"
$res += "T1: $(if ($p1) { 'PASS' } else { 'FAIL' })"; if (-not $p1) { $ok = $false }

# ---- T2
$res += ""; $res += "--- T2 rally (the scripted hand plays every ball) for $RallySeconds s"
$userShots = @{}; $partnerShots = @{}; $best = 0; $phases = @{}; $modes = @{}; $shotTaken = $false; $trace = @()
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($p1 -and $sw.Elapsed.TotalSeconds -lt $RallySeconds) {
    $t = Tennis
    if (-not $t) { break }
    $phases[$t.phase] = 1; $modes[(PcMode)] = 1
    if ($t.shot -match "^user#(\d+)") { $userShots[$Matches[1]] = 1 }
    if ($t.shot -match "^partner#(\d+)") { $partnerShots[$Matches[1]] = 1 }
    if ($t.best -gt $best) { $best = $t.best }
    $line = "{0,5:0.0}s {1,-6} {2}:{3} rally={4} shot={5} {6}" -f $sw.Elapsed.TotalSeconds, $t.phase, $t.user, $t.partner, $t.rally, $t.shot, $t.note
    if ($trace.Count -eq 0 -or $trace[-1].Substring(7) -ne $line.Substring(7)) { $trace += $line }
    if (-not $shotTaken -and $t.phase -eq "rally" -and $sw.Elapsed.TotalSeconds -gt 8) {
        $shotTaken = $true
        & $ctl debug simShot 0 0 0 (Join-Path $root "$ShotDir\tennis-mr-sim.png") | Out-Null
    }
    Start-Sleep -Milliseconds 120
}
$res += $trace | Select-Object -First 70
$res += "strokes seen: user=$($userShots.Count) partner=$($partnerShots.Count)  best rally=$best  phases=$(($phases.Keys | Sort-Object) -join ',')  pc modes=$(($modes.Keys | Sort-Object) -join ',')"
$p2 = ($userShots.Count -ge 3) -and ($partnerShots.Count -ge 3) -and ($best -ge 2) -and $modes.ContainsKey("Tennis")
$res += "T2: $(if ($p2) { 'PASS' } else { 'FAIL' })"; if (-not $p2) { $ok = $false }

# ---- T3
$res += ""; $res += "--- T3 the hand stops playing: the partner takes the points and the game ends"
Hand "tennisMiss"
$last = $null; $sawOver = $false
for ($i = 0; $i -lt 240; $i++) { $t = Tennis; if (-not $t) { break }; $last = $t; if ($t.phase -eq "over") { $sawOver = $true }; Start-Sleep -Milliseconds 500 }
$ended = -not (Tennis)
$res += "last seen: $(if ($last) { "phase=$($last.phase) user=$($last.user) partner=$($last.partner) note='$($last.note)'" } else { 'nothing' })  game over seen=$sawOver  ended=$ended"
$p3 = $ended -and $sawOver -and $last -and ($last.partner -ge 5 -or $last.user -ge 5)
$res += "T3: $(if ($p3) { 'PASS' } else { 'FAIL' })"; if (-not $p3) { $ok = $false }
Start-Sleep -Seconds 3
$modeAfter = PcMode
$res += "pc mode after the game: $modeAfter"

# ---- T4
$res += ""; $res += "--- T4 a second game, stopped from the room in the middle of a rally"
Hand "tennis"
& $ctl tennis start | Out-Null
$t = WaitGame 20
for ($i = 0; $i -lt 40 -and $t -and $t.phase -ne "rally"; $i++) { Start-Sleep -Milliseconds 300; $t = Tennis }
Start-Sleep -Seconds 4
& $ctl tennis stop | Out-Null
$stopped = WaitNoGame 5
Start-Sleep -Seconds 3
$res += "second game started=$($null -ne $t)  stopped within 5 s=$stopped  pc mode after=$(PcMode)"
$p4 = ($null -ne $t) -and $stopped -and ((PcMode) -ne "Tennis")
$res += "T4: $(if ($p4) { 'PASS' } else { 'FAIL' })"; if (-not $p4) { $ok = $false }
Hand "none"

# ---- T5
$res += ""; $res += "--- T5 afterwards: back out to the desk"
& $ctl return 0 0 | Out-Null; & $ctl wait 60 | Out-Null
Start-Sleep -Seconds 3
$head = ((Status) -split "`n")[0]
$p5 = $head -match "owner=mr"
$res += "end: $head"
$res += "T5: $(if ($p5) { 'PASS' } else { 'FAIL' })"; if (-not $p5) { $ok = $false }

$res += ""
$res += "RESULT: $(if ($ok) { 'PASS' } else { 'FAIL' })"
$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res | Select-Object -Last 14
