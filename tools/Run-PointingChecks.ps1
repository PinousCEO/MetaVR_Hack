# Pointing: does the spot the partner understands match the spot the hand points at?
# Part A - for a grid of spots on every calibrated display, a scripted hand ray is aimed at the spot and the pointing
#          sample the host receives is compared with it (in display pixels). This exercises the ray -> calibrated display
#          frame -> (u,v) -> pixel chain of the shared MR code, including rotated displays and negative coordinates.
# Part B - on the displays given in -GoIn, the hand points, pinches (preview), pinches again (go): the pixel the partner
#          actually goes in at is compared with the pointed pixel, then it comes back out.
# The hand here is the SIMULATOR's scripted ray: how well a real tracked hand produces that ray is not measured by this.
# Needs: tools\Start-Dev.ps1 -Sim ... running.
param([string]$Out = "artifacts\pointing-checks.txt", [string[]]$GoIn = @("DISPLAY3", "DISPLAY1"), [double]$MaxPx = 6)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
function Head { (& $ctl status | Select-Object -First 1) }
function Monitors {
    & $ctl status | Select-String "(DISPLAY\d+) bounds=\[(-?\d+),(-?\d+) (\d+)x(\d+)\].* rot=(\d+)" | ForEach-Object {
        $g = $_.Matches[0].Groups
        [pscustomobject]@{ name = $g[1].Value; x = [double]$g[2].Value; y = [double]$g[3].Value; w = [double]$g[4].Value; h = [double]$g[5].Value; rot = $g[6].Value }
    }
}
function Pointer {
    $t = (& $ctl pointer) -join " "
    if ($t -match '"monitorId":"([^"]*)".*?"u":(-?[\d.eE-]+),"v":(-?[\d.eE-]+).*?"valid":(true|false)') { [pscustomobject]@{ mon = $Matches[1]; u = [double]$Matches[2]; v = [double]$Matches[3]; valid = $Matches[4] -eq "true" } }
}
function TargetPx {
    $raw = (& $ctl snapshot) -join "`n"
    if ($raw -match '(?s)"target":\s*\{.*?"pxX":\s*(-?[\d.]+),\s*"pxY":\s*(-?[\d.]+)') { @([double]$Matches[1], [double]$Matches[2]) } else { @(0, 0) }
}

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 2
$res = @("Pointing checks $(Get-Date -Format s)  (the pointing hand is the simulator's scripted ray - NOT a tracked hand on a Quest)")
$ok = $true
$mons = @(Monitors)

$res += ""; $res += "--- A: pointed spot vs the pointing sample the host received (display pixels)"
$grid = @(0.08, 0.5, 0.92)
$worst = 0.0; $n = 0; $bad = 0
foreach ($m in $mons) {
    $line = "{0,-9} {1}x{2} rot={3}: " -f $m.name, $m.w, $m.h, $m.rot
    foreach ($v in $grid) { foreach ($u in $grid) {
        & $ctl debug simPoint 3 $u $v $m.name | Out-Null   # the scripted hand holds the spot for 3 s; it is read while it still does
        Start-Sleep -Milliseconds 1100                     # the pointer is smoothed against hand tremor: let it settle
        $p = Pointer
        if ($p -and $p.valid -and $p.mon -like "*$($m.name)") {
            $e = [math]::Sqrt([math]::Pow(($p.u - $u) * $m.w, 2) + [math]::Pow(($p.v - $v) * $m.h, 2))
            $n++; if ($e -gt $worst) { $worst = $e }; if ($e -gt $MaxPx) { $bad++ }
            $line += ("{0:0.0} " -f $e)
        } else { $bad++; $n++; $line += "MISS " }
        Start-Sleep -Milliseconds 200
    } }
    $res += $line + " px"
}
$res += ("spots={0}  worst error={1:0.0} px  over {2} px or missed={3}" -f $n, $worst, $MaxPx, $bad)
$pa = ($bad -eq 0) -and ($n -gt 0); if (-not $pa) { $ok = $false }
$res += "A: $(if ($pa) { 'PASS' } else { 'FAIL' })"

$res += ""; $res += "--- B: point, pinch (preview), pinch again (go): where the partner really goes in"
foreach ($name in $GoIn) {
    $m = $mons | Where-Object { $_.name -eq $name }
    if (-not $m) { $res += "${name}: not connected - skipped"; continue }
    $u = 0.64; $v = 0.42
    $wantX = $m.x + $u * $m.w; $wantY = $m.y + $v * $m.h
    & $ctl debug simPoint 6 $u $v $name | Out-Null
    Start-Sleep -Milliseconds 1200; & $ctl debug simPinch | Out-Null
    Start-Sleep -Milliseconds 1500; & $ctl debug simPinch | Out-Null
    Start-Sleep -Seconds 3
    & $ctl wait 60 | Out-Null
    $t = TargetPx
    $e = [math]::Sqrt([math]::Pow($t[0] - $wantX, 2) + [math]::Pow($t[1] - $wantY, 2))
    $inPc = (Head) -match "owner=pc"
    $res += ("{0,-9} pointed ({1:0},{2:0})  went in at ({3:0},{4:0})  difference {5:0.0} px  inside the PC={6}" -f $name, $wantX, $wantY, $t[0], $t[1], $e, $inPc)
    if (-not ($inPc -and $e -le $MaxPx)) { $ok = $false }
    Start-Sleep -Seconds 2
    & $ctl return 0 0 | Out-Null; & $ctl wait 60 | Out-Null
    Start-Sleep -Seconds 5
}
$res += "end: " + (Head)
$res += ""
$res += "RESULT: $(if ($ok) { 'PASS' } else { 'FAIL' })"
$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res
