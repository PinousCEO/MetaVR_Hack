# Returns the PC to a clean product state after development/verification runs:
# stops the host and clients, closes the sample window, and removes the host's saved state (owner, simulator
# calibrations, test timers and tasks). Logs, captures and the sample project are kept unless -All is given.
param([switch]$All)
$root = Split-Path -Parent $PSScriptRoot
$data = Join-Path $env:LOCALAPPDATA "LivingAIPartner"
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "WinCtl.ps1") -Action close -Title "Momo Notes" 2>$null | Out-Null
Start-Sleep -Milliseconds 600
Get-Process -Name "LivingPartner.Host", "LivingAIPartner" -ErrorAction SilentlyContinue | ForEach-Object { try { $_ | Stop-Process -Force } catch { } }
Start-Sleep -Milliseconds 500
foreach ($f in @("state.json", "state.json.tmp")) { $p = Join-Path $data $f; if (Test-Path $p) { Remove-Item $p -Force } }
if ($All -and (Test-Path $data)) { Remove-Item $data -Recurse -Force }
Write-Output "clean: host/clients stopped, sample window closed, saved state removed$(if ($All) { ' (all runtime data removed)' })"
