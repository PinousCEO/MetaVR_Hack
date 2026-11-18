# Pairing flow check through the desktop MR simulator (NOT a Quest):
# the simulator starts like a device that was never paired (no token), the hand numpad appears, a scripted fingertip
# types a WRONG code and presses connect (must be refused), then types the code shown by the host and presses connect
# (must be accepted and the client must come up). The code itself is never written to this file or to any log.
# Needs: host running without a simulator (tools\Start-Dev.ps1 -LoopbackOnly), Windows client build in dist\.
param([string]$Out = "artifacts\pairing-flow.txt", [int]$SimX = -490, [int]$SimY = -800, [int]$SimW = 480, [int]$SimH = 430)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$client = Join-Path $root "dist\LivingAIPartner-Windows\LivingAIPartner.exe"
$data = Join-Path $env:LOCALAPPDATA "LivingAIPartner"
$paired = Join-Path $data "paired.json"
$hostLog = Join-Path $data "logs\host.log"
$simLog = Join-Path $data "logs\mr-sim.log"
function SimProcs { Get-CimInstance Win32_Process -Filter "Name='LivingAIPartner.exe'" | Where-Object { $_.CommandLine -match "-lp-role mrsim" } }

$res = @("Pairing flow check $(Get-Date -Format s)  (MR client: desktop simulator started WITHOUT a token; numpad pressed by its scripted fingertip - not a Quest)")
SimProcs | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
$backup = if (Test-Path $paired) { [IO.File]::ReadAllText($paired) } else { $null }
$n0 = (Get-Content $hostLog).Count
$code = ("" + (& $ctl paircode)).Trim()
if ($code -notmatch "^\d{6}$") { $res += "could not read the pairing code from the host (lpctl paircode)"; $res | Set-Content (Join-Path $root $Out) -Encoding utf8; $res; exit 2 }
$wrong = "{0:D6}" -f (([int]$code + 123457) % 1000000)

Add-Type -Namespace LpPair -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();' -ErrorAction SilentlyContinue
$fg = [LpPair.U]::GetForegroundWindow().ToInt64()
Start-Process -FilePath $client -ArgumentList "-lp-role mrsim -lp-sim-x $SimX -lp-sim-y $SimY -lp-sim-w $SimW -lp-sim-h $SimH -lp-no-token -lp-sim-pair ${wrong}c${code}c -lp-return-focus $fg -logFile `"$simLog`"" | Out-Null
$res += "simulator started with -lp-no-token; scripted keys: 6 wrong digits, connect, 6 digits of the host's code, connect"

Start-Sleep -Seconds 8
& (Join-Path $PSScriptRoot "Capture-Screen.ps1") -X $SimX -Y $SimY -W $SimW -H $SimH -Out "artifacts\pairing-numpad.png" | Out-Null
$res += "numpad capture while typing: artifacts\pairing-numpad.png"
$joined = $false
for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Seconds 1; if ((& $ctl status) -match "clients=\[[^\]]*sim") { $joined = $true; break } }
Start-Sleep -Seconds 2

$hostLines = @(Get-Content $hostLog | Select-Object -Skip $n0 | Where-Object { $_ -match "pair attempt|client connected: role=sim" })
$res += "host log:"
$hostLines | ForEach-Object { $res += "  " + $_ }
$res += "simulator log:"
@(Select-String -Path $simLog -Pattern "\[SIM\] scripted fingertip pressed|\[MR\] host connection" | ForEach-Object { $_.Line }) | ForEach-Object { $res += "  " + $_ }
$res += "status: " + ((& $ctl status | Select-Object -First 2) -join " | ")
& (Join-Path $PSScriptRoot "Capture-Screen.ps1") -X $SimX -Y $SimY -W $SimW -H $SimH -Out "artifacts\pairing-after.png" | Out-Null

$text = $hostLines -join "`n"
$refused = $text -match "pair attempt[^\n]*wrong code"
$accepted = $text -match "pair attempt[^\n]*: ok"
$connected = $text -match "client connected: role=sim"
$order = $refused -and $accepted -and ($text.IndexOf("wrong code") -lt $text.IndexOf(": ok"))
$ok = $order -and $connected -and $joined
$res += "wrong code refused: $refused   right code accepted: $accepted   client came up afterwards: $($connected -and $joined)"
$res += "pairing flow: " + $(if ($ok) { "PASS" } else { "FAIL" })

# leave no test pairing behind: stop the simulator and put paired.json back (the host forgets its in-memory copy when it restarts)
SimProcs | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if ($null -eq $backup) { if (Test-Path $paired) { Remove-Item $paired -Force } } else { [IO.File]::WriteAllText($paired, $backup) }
$res += "cleanup: simulator stopped, paired.json restored to its state before the check (restart the host to drop the in-memory test token)"
$res | Set-Content (Join-Path $root $Out) -Encoding utf8
$res
if ($ok) { exit 0 } else { exit 5 }
