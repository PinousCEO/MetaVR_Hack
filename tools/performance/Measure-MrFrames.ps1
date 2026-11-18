param(
    [string]$Player = (Join-Path $PSScriptRoot '../../dist/Yonder/LivingAIPartner-Windows/LivingAIPartner.exe')
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Player).Path
$scenario = Join-Path $PSScriptRoot 'mr-scenes.txt'
$run = Join-Path ([IO.Path]::GetTempPath()) ('YonderFrames-' + [guid]::NewGuid().ToString('N'))
$null = New-Item $run -ItemType Directory
$log = Join-Path $run 'player.log'
$save = Join-Path $run 'world.json'
$launch = @(
    '-lp-role mrsim -lp-local -lp-no-mic -screen-fullscreen 0',
    '-lp-sim-x -22000 -lp-sim-y -22000 -lp-sim-w 1280 -lp-sim-h 720',
    ('-lp-local-script "{0}" -lp-local-file "{1}" -logFile "{2}"' -f $scenario,$save,$log)
)
$process = Start-Process -FilePath $exe -ArgumentList $launch -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(140000)) {
    if (!$process.HasExited) { $process.Kill() }
    throw "Simulator timed out. See $log"
}
$rows = @(Select-String -LiteralPath $log -Pattern '^\[MR\] frames (\S+) n=')
$expected = @('desk', 'open-menu', 'first-open', 'growing', 'demon-wave', 'ripe-garden')
$actual = @($rows | ForEach-Object { $_.Matches[0].Groups[1].Value })
if ($process.ExitCode -ne 0 -or ($actual -join ',') -ne ($expected -join ',')) {
    throw "Incomplete run. See $log"
}
$rows | ForEach-Object { $_.Line }
Write-Output "Player log saved at $log"
