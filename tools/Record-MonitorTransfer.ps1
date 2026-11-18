# Records the PC-internal display-to-display move between two STACKED displays (down through the shared edge, then back
# up), as two short continuous screen captures of a strip that spans the edge. PC side only: no MR client is involved.
# Needs: host + Windows character client running (tools\Start-Dev.ps1 -Monitor <upper display>), partner idle.
param([string]$Upper = "DISPLAY3", [string]$Lower = "DISPLAY2", [int]$EdgeY = 0, [string]$OutPrefix = "artifacts\monitor-transfer-stacked")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
function Pc { $l = (& $ctl status | Select-String "^\s*pc:"); if ($l) { $l.Line.Trim() } else { "pc: (no client)" } }
function Clip([string]$name, [int]$fx, [string]$target) {
    $out = Join-Path $root "$OutPrefix-$name.mp4"
    if (Test-Path $out) { Remove-Item $out -Force }
    $x = $fx - 320; $y = $EdgeY - 440
    $a = "-y -loglevel error -f gdigrab -framerate 30 -offset_x $x -offset_y $y -video_size 640x880 -i desktop -t 9 -c:v libx264 -preset veryfast -pix_fmt yuv420p -crf 22 `"$out`""
    $rec = Start-Process -FilePath $ffmpeg -ArgumentList $a -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 1
    & $ctl debug pcGoMonitor 0 0 0 $target | Out-Null
    $rec.WaitForExit(30000) | Out-Null
    & $ffmpeg -y -loglevel error -ss 1.1 -t 1.6 -i $out -vf "fps=15,scale=160:-1,tile=12x2" -frames:v 1 (Join-Path $root "$OutPrefix-$name-sheet.png")   # 24 consecutive frames at 15 fps around the move
    "$name -> " + (Pc)
}

$res = @("Stacked display-to-display move $(Get-Date -Format s)  ($Upper above $Lower; PC side only)")
& $ctl owner pc | Out-Null; Start-Sleep -Seconds 4
$s = Pc; $res += "start: $s"
$m = [regex]::Match($s, "feetPx=\((-?\d+),(-?\d+)\)"); if (-not $m.Success) { $res += "no PC client"; $res; exit 2 }
$res += Clip "down" ([int]$m.Groups[1].Value) $Lower
Start-Sleep -Seconds 2
$s = Pc; $m = [regex]::Match($s, "feetPx=\((-?\d+),(-?\d+)\)")
$res += Clip "up" ([int]$m.Groups[1].Value) $Upper
Start-Sleep -Seconds 2
$res += "end: " + (Pc)
& $ctl owner mr | Out-Null
$res += (Select-String -Path (Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\pc-client.log") -Pattern "MonitorTransfer" | Select-Object -Last 4 | ForEach-Object { "  " + $_.Line })
$res | Set-Content -Path (Join-Path $root "$OutPrefix.txt") -Encoding utf8
$res
