# Repeats portal crossings (MR -> PC -> MR ...) at several screen positions and summarises the measured results
# from the host's transitions.jsonl. Requires the host, the Windows character client and an MR client
# (a Quest, or the desktop simulator started with tools\Start-Dev.ps1 -Sim) to be running.
param(
    [int]$RoundTrips = 5,
    # physical pixels in the virtual desktop; defaults target the display above the primary one on the dev machine
    [string[]]$Points = @("500,-700", "1350,-420", "960,-860", "300,-300", "1600,-650"),
    [string]$Out = "artifacts\portal-loop.txt"
)
$root = Split-Path -Parent $PSScriptRoot
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$log = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\transitions.jsonl"
$before = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
& $ctl play | Out-Null
& $ctl owner mr | Out-Null
Start-Sleep -Milliseconds 500
$results = @()
for ($i = 0; $i -lt $RoundTrips; $i++) {
    $p = $Points[$i % $Points.Count].Split(",")
    $q = $Points[($i + 2) % $Points.Count].Split(",")
    & $ctl enter $p[0] $p[1] | Out-Null
    & $ctl wait 40 | Out-Null
    $results += "enter  ($($p[0]),$($p[1])) exit=$LASTEXITCODE"
    Start-Sleep -Milliseconds 800
    & $ctl return $q[0] $q[1] | Out-Null
    & $ctl wait 40 | Out-Null
    $results += "return ($($q[0]),$($q[1])) exit=$LASTEXITCODE"
    Start-Sleep -Milliseconds 800
}
$lines = @()
if (Test-Path $log) { $lines = Get-Content $log | Select-Object -Skip $before }
$recs = $lines | ForEach-Object { $_ | ConvertFrom-Json }
$done = @($recs | Where-Object { $_.result -eq "Complete" })
$fail = @($recs | Where-Object { $_.result -ne "Complete" })
$summary = @()
$summary += "Portal loop $(Get-Date -Format s): requested=$($RoundTrips * 2) recorded=$($recs.Count) complete=$($done.Count) notComplete=$($fail.Count)"
$summary += "MR client simulated: " + (($recs | Select-Object -ExpandProperty mrSimulated -Unique) -join ",")
foreach ($r in $recs) {
    $summary += ("  {0} dir={1} {2} variant={3} mon={4} px=({5:0},{6:0}) anchor={7} plan={8:0.00}s wall={9:0.00}s tauErr pc={10:0.0}ms mr={11:0.0}ms beats pc={12} mr={13} {14}" -f `
        $r.transitionId, $r.direction, $r.result, $r.variant, ($r.monitor -replace '\\\\\.\\', ''), $r.px, $r.py, $r.anchor, $r.planTotal, $r.wallSeconds, $r.maxTauErrPcMs, $r.maxTauErrMrMs, $r.beatsPc, $r.beatsMr, $r.reason)
}
if ($done.Count -gt 0) {
    $summary += ("max shared-clock error over completed crossings: pc={0:0.0}ms mr={1:0.0}ms" -f ($done | Measure-Object maxTauErrPcMs -Maximum).Maximum, ($done | Measure-Object maxTauErrMrMs -Maximum).Maximum)
}
$summary += $results
$outPath = Join-Path $root $Out
$summary | Set-Content -Path $outPath -Encoding utf8
$summary
