# Captures a physical-pixel region of the virtual desktop to a PNG (DPI aware). Used for verification evidence.
param(
    [int]$X = 0, [int]$Y = 0, [int]$W = 1920, [int]$H = 1080,
    [string]$Out = "artifacts\capture.png",
    [double]$Scale = 1.0
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LpDpi {
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
}
"@ -ErrorAction SilentlyContinue
[void][LpDpi]::SetThreadDpiAwarenessContext([IntPtr](-4))
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size($W, $H)))
$g.Dispose()
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
if ($Scale -ne 1.0) {
    $nw = [int]($W * $Scale); $nh = [int]($H * $Scale)
    $small = New-Object System.Drawing.Bitmap($bmp, (New-Object System.Drawing.Size($nw, $nh)))
    $small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $small.Dispose()
} else {
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
}
$bmp.Dispose()
Write-Output "saved $Out ($W x $H @ $X,$Y)"
