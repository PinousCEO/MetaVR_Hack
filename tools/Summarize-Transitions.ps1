# Summarises the host's transition log (every portal transition of the session) into artifacts\transition-stats.txt
# and copies the raw log next to it. Numbers in the reports come from this file.
param([string]$Out = "artifacts\transition-stats.txt", [string]$Since = "")
$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\transitions.jsonl"
$recs = @(Get-Content $log | ForEach-Object { try { $_ | ConvertFrom-Json } catch { } })
Copy-Item $log (Join-Path $root "artifacts\transitions.jsonl") -Force

function Stats($set, [string]$label) {
    $done = @($set | Where-Object { $_.result -eq "Complete" })
    $lines = @("== $label")
    $lines += "transitions recorded: $($set.Count)   Complete: $($done.Count)   not completed: $($set.Count - $done.Count)"
    $lines += "direction (completed): MR->PC $(@($done | Where-Object { $_.direction -eq 0 }).Count), PC->MR $(@($done | Where-Object { $_.direction -eq 1 }).Count)"
    $lines += "displays (all): " + ((@($set | Group-Object monitor | ForEach-Object { ($_.Name -replace '\\\\\.\\', '') + " " + $_.Count })) -join ", ")
    $lines += "anchors (all): " + ((@($set | Group-Object anchor | ForEach-Object { $_.Name + " " + $_.Count })) -join ", ")
    $lines += "variants (all): " + ((@($set | Group-Object variant | ForEach-Object { $_.Name + " " + $_.Count })) -join ", ")
    $lines += "distinct portal pixels: " + @($set | ForEach-Object { "$([math]::Round($_.px)),$([math]::Round($_.py))" } | Sort-Object -Unique).Count
    # normal-speed crossings only: debug freeze / slow-motion / rewind runs take much longer than the plan
    $normal = @($done | Where-Object { $_.beatsPc -gt 0 -and $_.beatsMr -gt 0 -and $_.wallSeconds -lt ($_.planTotal + 12) })
    if ($normal.Count -gt 0) {
        $pc = $normal | Measure-Object maxTauErrPcMs -Maximum -Average
        $mr = $normal | Measure-Object maxTauErrMrMs -Maximum -Average
        $lines += ("shared-clock error over {0} normal-speed completed crossings: PC max {1:0.0} ms / mean {2:0.00} ms; MR max {3:0.0} ms / mean {4:0.00} ms" -f $normal.Count, $pc.Maximum, $pc.Average, $mr.Maximum, $mr.Average)
    }
    # the flag is read from the MR client connected when the transition ends; it is false when that client had been
    # killed on purpose (recovery case D). No Quest was ever connected in this session.
    $noMr = @($set | Where-Object { -not $_.mrSimulated })
    $lines += "MR client: desktop simulator in $($set.Count - $noMr.Count); no MR client connected at the end in $($noMr.Count) (simulator killed mid-crossing by recovery test D): " + (($noMr | ForEach-Object { $_.transitionId }) -join ", ")
    foreach ($r in @($set | Where-Object { $_.result -ne "Complete" })) { $lines += "  not completed: $($r.t) $($r.transitionId) dir=$($r.direction) result=$($r.result) reason='$($r.reason)'" }
    return $lines
}

$res = @("Transition statistics $(Get-Date -Format s)  (source: transitions.jsonl written by the host; MR side = desktop simulator, never a Quest)", "")
$res += Stats $recs "whole session"
if ($Since.Length -gt 0) {
    $res += ""
    $res += Stats @($recs | Where-Object { $_.t -ge $Since }) "since $Since"
}
$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res
