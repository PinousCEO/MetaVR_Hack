# Stops the host, the hub, the character client and the simulator (development helper).
# The clients are asked to close first and only then stopped by force, and the script REPORTS any process that is
# still listed afterwards. On 2026-11-17 dozens of clients that had been stopped stayed behind as processes that
# could not be ended (one thread stuck in a kernel wait, 0.8 GB of committed memory each); together they filled the
# page file and with it the disk. Only a restart of Windows removed them. If this script ever says that processes
# were left behind: stop testing and restart the PC before they pile up.
$clients = @(Get-Process -Name "LivingAIPartner" -ErrorAction SilentlyContinue)
foreach ($p in $clients) { try { if ($p.MainWindowHandle -ne 0) { [void]$p.CloseMainWindow() } } catch { } }
if ($clients.Count -gt 0) { Start-Sleep -Milliseconds 1500 }
Get-Process -Name "LivingPartner.Host", "LivingPartner.MarketHub", "LivingAIPartner" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_ | Stop-Process -Force } catch { }
}
Start-Sleep -Seconds 3
$left = @(Get-Process -Name "LivingAIPartner", "LivingPartner.Host", "LivingPartner.MarketHub" -ErrorAction SilentlyContinue)
if ($left.Count -gt 0) {
    $gb = ($left | Measure-Object PrivateMemorySize64 -Sum).Sum / 1GB
    Write-Output ("stopped - BUT {0} process(es) are still listed and hold {1:0.0} GB: they cannot be ended; restart Windows before running more tests" -f $left.Count, $gb)
} else { Write-Output "stopped" }
