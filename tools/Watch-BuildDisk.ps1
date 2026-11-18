# Guards drive C: while Unity builds the Quest APK. A full Android (IL2CPP) build writes several GB of intermediate
# files; on 2026-11-17 the disk filled up, the build failed with "Win32 IO returned 112" and the Editor stood behind a
# "Low disk space" dialog. This script watches the free space and, when it falls under -MinGB, stops the build's
# worker programs (the build then fails cleanly instead of filling the disk) and says so in its log.
#   Start-Process powershell -WindowStyle Hidden -ArgumentList "-ExecutionPolicy Bypass -File tools\Watch-BuildDisk.ps1"
param([double]$MinGB = 0.8, [int]$Minutes = 40)
$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $root "artifacts\build-disk-watch.txt"
$apk = Join-Path $root "dist\LivingAIPartner-Quest\LivingAIPartner.apk"
$start = Get-Date
$low = [double]::MaxValue
Set-Content $log ("{0:HH:mm:ss} watching C: (stop the build under {1} GB), free now {2:0.0} GB" -f $start, $MinGB, ((Get-PSDrive C).Free / 1GB)) -Encoding utf8
while (((Get-Date) - $start).TotalMinutes -lt $Minutes) {
    $free = (Get-PSDrive C).Free / 1GB
    if ($free -lt $low) { $low = $free }
    if ($free -lt $MinGB) {
        $killed = @()
        Get-Process bee_backend, il2cpp, UnityLinker, clang, "clang++", "ld.lld", lld, "llvm-objcopy" -ErrorAction SilentlyContinue | ForEach-Object { $killed += $_.Name; Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Get-Process java -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*Unity*" } | ForEach-Object { $killed += "java"; Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Add-Content $log ("{0:HH:mm:ss} C: down to {1:0.00} GB - stopped the build's workers: {2}" -f (Get-Date), $free, ($killed -join ", ")) -Encoding utf8
        exit 2
    }
    if ((Test-Path $apk) -and (Get-Item $apk).LastWriteTime -gt $start) {
        Add-Content $log ("{0:HH:mm:ss} a new APK was written; lowest free space during the build {1:0.0} GB" -f (Get-Date), $low) -Encoding utf8
        exit 0
    }
    Start-Sleep -Seconds 4
}
Add-Content $log ("{0:HH:mm:ss} gave up waiting; lowest free space {1:0.0} GB" -f (Get-Date), $low) -Encoding utf8
