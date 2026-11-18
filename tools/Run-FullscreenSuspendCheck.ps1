# Checks that the desktop character gets out of the way while a full-screen application is in the foreground on its
# display, and comes back when that application is gone. A plain borderless black window covering the display stands in
# for the full-screen application for about three seconds. It is activated with the normal API only; if Windows refuses
# to give this script the foreground, the check reports NOT_TESTED instead of forcing it.
# Needs: host + Windows character client running (tools\Start-Dev.ps1 -Monitor <display>), no portal transition active.
param([int]$X = 0, [int]$Y = -1080, [int]$W = 1920, [int]$H = 1080, [string]$Out = "artifacts\fullscreen-suspend-check.txt")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace LpFs -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);'
function Pc { $l = (& $ctl status | Select-String "^\s*pc:"); if ($l) { $l.Line.Trim() } else { "pc: (no client)" } }
function Pump([double]$seconds) { $end = (Get-Date).AddSeconds($seconds); while ((Get-Date) -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 } }

$res = @("Full-screen suspension check $(Get-Date -Format s)  (stand-in full-screen window at [$X,$Y ${W}x$H]; PC side only)")
& $ctl owner pc | Out-Null; Start-Sleep -Seconds 4
$before = Pc; $res += "before: $before"
$prev = [LpFs.U]::GetForegroundWindow()

$f = New-Object System.Windows.Forms.Form
$f.FormBorderStyle = "None"; $f.StartPosition = "Manual"; $f.ShowInTaskbar = $false; $f.BackColor = [System.Drawing.Color]::Black; $f.Text = "LivingPartner full-screen check"
$f.Bounds = New-Object System.Drawing.Rectangle($X, $Y, $W, $H)
$f.Show(); $f.Activate(); Pump 0.6
$isFg = ([LpFs.U]::GetForegroundWindow() -eq $f.Handle)
$res += "stand-in window is the foreground window: $isFg"
Pump 2.4
$during = Pc; $res += "while it covers the display: $during"
$f.Close(); $f.Dispose()
[void][LpFs.U]::SetForegroundWindow($prev)
Start-Sleep -Seconds 2
$after = Pc; $res += "after it closed: $after"
& $ctl owner mr | Out-Null

if (-not $isFg) { $verdict = "NOT_TESTED (Windows did not give the stand-in window the foreground; nothing was forced)" }
elseif (($before -match "visible=True") -and ($during -match "mode=Suspended") -and ($during -match "visible=False") -and ($after -match "visible=True") -and ($after -notmatch "mode=Suspended")) { $verdict = "PASS" }
else { $verdict = "FAIL" }
$res += "full-screen suspension: $verdict"
$res | Set-Content (Join-Path $root $Out) -Encoding utf8
$res
