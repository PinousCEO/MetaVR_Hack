# Window helper for verification: find a top-level window by title fragment and move / minimise / restore it,
# or list the z-order around the character overlay. Only used to exercise the product from outside (no injection).
param(
    [Parameter(Mandatory = $true)][ValidateSet("find", "move", "minimize", "restore", "close", "zorder", "front", "topmost", "notopmost")][string]$Action,
    [string]$Title = "",
    [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0
)
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class LpW {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    public static string Title(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
    public static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static IntPtr Find(string part) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { if (found == IntPtr.Zero && IsWindowVisible(h) && Title(h).IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) found = h; return true; }, IntPtr.Zero);
        return found;
    }
}
"@ -ErrorAction SilentlyContinue
[void][LpW]::SetThreadDpiAwarenessContext([IntPtr](-4))
if ($Action -eq "zorder") {
    $i = 0
    for ($hw = [LpW]::GetTopWindow([IntPtr]::Zero); $hw -ne [IntPtr]::Zero; $hw = [LpW]::GetWindow($hw, 2)) {
        if (-not [LpW]::IsWindowVisible($hw)) { continue }
        $wt = [LpW]::Title($hw); $c = [LpW]::Class($hw)
        $r = New-Object LpW+RECT; [void][LpW]::GetWindowRect($hw, [ref]$r)
        if ($wt -eq "" -and $c -ne "Static") { continue }
        if ($i -lt 10 -or $wt -eq "LivingPartnerOverlay" -or ($Title -and $wt -like "*$Title*")) {
            Write-Output ("z{0:00} hwnd=0x{1:X} class={2} title='{3}' rect=[{4},{5} {6}x{7}]" -f $i, $hw.ToInt64(), $c, $(if ($wt.Length -gt 40) { $wt.Substring(0, 40) } else { $wt }), $r.l, $r.t, ($r.r - $r.l), ($r.b - $r.t))
        }
        $i++
    }
    exit 0
}
$hwnd = [LpW]::Find($Title)
if ($hwnd -eq [IntPtr]::Zero) { Write-Output "window '$Title' not found"; exit 1 }
switch ($Action) {
    "find" { $r = New-Object LpW+RECT; [void][LpW]::GetWindowRect($hwnd, [ref]$r); Write-Output ("hwnd=0x{0:X} title='{1}' rect=[{2},{3} {4}x{5}] minimized={6}" -f $hwnd.ToInt64(), [LpW]::Title($hwnd), $r.l, $r.t, ($r.r - $r.l), ($r.b - $r.t), [LpW]::IsIconic($hwnd)) }
    "move" { $flags = 0x0014; if ($W -le 0 -or $H -le 0) { $flags = $flags -bor 0x0001 }; [void][LpW]::SetWindowPos($hwnd, [IntPtr]::Zero, $X, $Y, $W, $H, $flags); Write-Output "moved to $X,$Y" }
    "minimize" { [void][LpW]::ShowWindow($hwnd, 7); Write-Output "minimized (without activating another window)" }
    "restore" { [void][LpW]::ShowWindow($hwnd, 4); Write-Output "restored (without activating)" }
    "front" { [void][LpW]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0013); Write-Output "raised to the top of the z-order (without activating)" }
    "topmost" { [void][LpW]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0013); Write-Output "set topmost (without activating)" }
    "notopmost" { [void][LpW]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0013); Write-Output "cleared topmost" }
    "close" { [void][LpW]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Write-Output "close requested" }
}



