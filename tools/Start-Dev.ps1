# Development launcher: Companion Host (which starts the Windows character client) + optional desktop MR simulator.
# The MR simulator is a verification tool, NOT a Quest. For normal use run Start-LivingPartner.ps1 instead.
param(
    [switch]$Sim,                       # also start the desktop MR simulator
    [switch]$LoopbackOnly,              # do not listen on the LAN (no firewall prompt); a Quest cannot connect in this mode
    [string]$Monitor = "",              # start display for the PC character, e.g. DISPLAY3
    [int]$SimX = -1040, [int]$SimY = -780, [int]$SimW = 1000, [int]$SimH = 700,
    [switch]$StubHarness,               # use the labelled test double instead of a real harness
    [string]$Harness = ""               # "codex" to use the Codex CLI adapter (default: Claude Code)
)
$root = Split-Path -Parent $PSScriptRoot
$hostExe = Join-Path $root "Companion\LivingPartner.Host\bin\Release\net8.0-windows\LivingPartner.Host.exe"
$distHost = Join-Path $root "dist\LivingAIPartner-Host\LivingPartner.Host.exe"
# development always runs the freshly built host; dist\ is only refreshed by publishing
if (-not (Test-Path $hostExe) -and (Test-Path $distHost)) { $hostExe = $distHost }
$client = Join-Path $root "dist\LivingAIPartner-Windows\LivingAIPartner.exe"
if (-not (Test-Path $hostExe)) { Write-Error "Host not built: $hostExe"; exit 1 }
if (-not (Test-Path $client)) { Write-Error "Windows character client not built: $client"; exit 1 }
$hostArgs = @("--client", "`"$client`"")
if ($LoopbackOnly) { $hostArgs += "--loopback-only" }
if ($StubHarness) { $hostArgs += "--stub-harness" }
if ($Harness) { $hostArgs += @("--harness", $Harness) }
if ($Monitor) { $hostArgs += @("--client-args", "`"-lp-monitor $Monitor`"") }
Start-Process -FilePath $hostExe -ArgumentList $hostArgs | Out-Null
Start-Sleep -Milliseconds 1500
Add-Type -Namespace LpFg -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();' -ErrorAction SilentlyContinue
$fg = [LpFg.U]::GetForegroundWindow().ToInt64()
if ($Sim) {
    Start-Process -FilePath $client -ArgumentList "-lp-role mrsim -lp-sim-x $SimX -lp-sim-y $SimY -lp-sim-w $SimW -lp-sim-h $SimH -lp-return-focus $fg -logFile `"$env:LOCALAPPDATA\LivingAIPartner\logs\mr-sim.log`"" | Out-Null
}
Write-Output ("started host" + $(if ($Sim) { ' + MR simulator' }))





