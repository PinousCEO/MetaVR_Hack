# WorkspaceBound checks on the real desktop: the partner enters a real program window, then the window is moved,
# minimised, restored and moved back (without activating it). The partner must follow by exactly the same pixels, hide
# while the window is minimised and come back in the same place. PC side is real; the entry is driven through the
# desktop MR simulator. Needs: tools\Start-Dev.ps1 -Sim ... running, partner in MR, the target window open.
param([string]$Title = "Momo Notes", [int]$Dx = -300, [int]$Dy = 120, [string]$Out = "artifacts\window-follow-checks.txt")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
function Win([string]$action, [int]$x = 0, [int]$y = 0, [int]$w = 0, [int]$h = 0) { powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "WinCtl.ps1") -Action $action -Title $Title -X $x -Y $y -W $w -H $h }
function Pc { $l = (& $ctl status | Select-String "^\s*pc:"); if ($l) { $l.Line.Trim() } else { "pc: (no client)" } }
function Feet([string]$s) { $m = [regex]::Match($s, "feetPx=\((-?\d+),(-?\d+)\)"); if ($m.Success) { @([int]$m.Groups[1].Value, [int]$m.Groups[2].Value) } else { @(0, 0) } }

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 2
$res = @("Window-follow checks $(Get-Date -Format s)  (real window '$Title' on the real desktop; entry driven through the desktop MR simulator)")
$r = Win "find"
$m = [regex]::Match($r, "hwnd=0x([0-9A-Fa-f]+).*rect=\[(-?\d+),(-?\d+) (\d+)x(\d+)\]")
if (-not $m.Success) { $res += "window not found: $r"; $res | Set-Content (Join-Path $root $Out) -Encoding utf8; $res; exit 2 }
$hwnd = $m.Groups[1].Value.ToUpper(); $wx = [int]$m.Groups[2].Value; $wy = [int]$m.Groups[3].Value; $ww = [int]$m.Groups[4].Value; $wh = [int]$m.Groups[5].Value
$res += "window: hwnd=0x$hwnd rect=[$wx,$wy ${ww}x$wh]"

& $ctl enter ($wx + [int]($ww * 0.5)) ($wy + [int]($wh * 0.45)) | Out-Null
& $ctl wait 40 | Out-Null
# The partner starts to wander on its own about 12 s after landing; all position comparisons below finish well before that.
Start-Sleep -Seconds 1
$t0 = Pc; $f0 = Feet $t0; $res += "T0 after entering the window: $t0"
$bound = ($t0 -match "mode=WorkspaceBound") -and ($t0.ToUpper() -match "BOUND=0X$hwnd")

Win "move" ($wx + $Dx) ($wy + $Dy) $ww $wh | Out-Null; Start-Sleep -Milliseconds 1200
$t1 = Pc; $f1 = Feet $t1; $res += "T1 window moved by ($Dx,$Dy): $t1"
$res += "   partner moved by ($($f1[0] - $f0[0]),$($f1[1] - $f0[1])) px"
$moved = ([math]::Abs(($f1[0] - $f0[0]) - $Dx) -le 2) -and ([math]::Abs(($f1[1] - $f0[1]) - $Dy) -le 2)

Win "minimize" | Out-Null; Start-Sleep -Milliseconds 1200
$t2 = Pc; $res += "T2 window minimised: $t2"
$hidden = ($t2 -match "mode=Hidden") -and ($t2 -match "visible=False") -and ($t2 -match "min=True")

Win "restore" | Out-Null; Start-Sleep -Milliseconds 1200
$t3 = Pc; $f3 = Feet $t3; $res += "T3 window restored: $t3"
$back = ($t3 -match "mode=WorkspaceBound") -and ($t3 -match "visible=True") -and ([math]::Abs($f3[0] - $f1[0]) -le 2) -and ([math]::Abs($f3[1] - $f1[1]) -le 2)

Win "move" $wx $wy $ww $wh | Out-Null; Start-Sleep -Milliseconds 1200
$t4 = Pc; $f4 = Feet $t4; $res += "T4 window moved back: $t4"
$atStart = ([math]::Abs($f4[0] - $f0[0]) -le 2) -and ([math]::Abs($f4[1] - $f0[1]) -le 2)

& $ctl return 0 0 | Out-Null
& $ctl wait 40 | Out-Null
$res += "returned: " + (& $ctl status | Select-Object -First 1)
$res += "bound to the window: $bound   followed the move exactly: $moved   hidden while minimised: $hidden   back in place after restore: $back   back at the start after moving back: $atStart"
$ok = $bound -and $moved -and $hidden -and $back -and $atStart
$res += "window follow: " + $(if ($ok) { "PASS" } else { "FAIL" })
$res | Set-Content (Join-Path $root $Out) -Encoding utf8
$res
if ($ok) { exit 0 } else { exit 6 }
