# Verifies that the character overlay window is a click-through, non-activating layered window (R01/R02).
# Prints the overlay rectangle, its extended styles and what Windows hit-tests at a pixel covered by the character.
param([string]$ProcessName = "LivingAIPartner")
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class LpWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    public static List<IntPtr> ForPid(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static string Title(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
    public static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
"@ -ErrorAction SilentlyContinue
[void][LpWin]::SetThreadDpiAwarenessContext([IntPtr](-4))
$procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $procs) { Write-Output "process $ProcessName not running"; exit 1 }
$fg = [LpWin]::GetForegroundWindow()
foreach ($p in $procs) {
    foreach ($h in [LpWin]::ForPid([uint32]$p.Id)) {
        $title = [LpWin]::Title($h); $cls = [LpWin]::Class($h)
        if ($title -ne "LivingPartnerOverlay") { Write-Output ("other window: class={0} title='{1}' visible={2}" -f $cls, $title, [LpWin]::IsWindowVisible($h)); continue }
        $r = New-Object LpWin+RECT; [void][LpWin]::GetWindowRect($h, [ref]$r)
        $ex = [LpWin]::GetWindowLongPtr($h, -20).ToInt64()
        $pt = New-Object LpWin+POINT; $pt.x = [int](($r.l + $r.r) / 2); $pt.y = [int](($r.t + $r.b) / 2)
        $hit = [LpWin]::WindowFromPoint($pt); $root = [LpWin]::GetAncestor($hit, 2)
        Write-Output ("overlay hwnd=0x{0:X} visible={1} rect=[{2},{3} {4}x{5}]" -f $h.ToInt64(), [LpWin]::IsWindowVisible($h), $r.l, $r.t, ($r.r - $r.l), ($r.b - $r.t))
        Write-Output ("  exstyle=0x{0:X} layered={1} transparent(click-through)={2} noactivate={3} toolwindow={4} topmost={5}" -f $ex, (($ex -band 0x80000) -ne 0), (($ex -band 0x20) -ne 0), (($ex -band 0x8000000) -ne 0), (($ex -band 0x80) -ne 0), (($ex -band 0x8) -ne 0))
        Write-Output ("  hit-test at overlay centre ({0},{1}) -> hwnd=0x{2:X} class={3} title='{4}' isOverlay={5}" -f $pt.x, $pt.y, $root.ToInt64(), [LpWin]::Class($root), [LpWin]::Title($root), ($root -eq $h))
        Write-Output ("  foreground window=0x{0:X} '{1}' overlayIsForeground={2}" -f $fg.ToInt64(), [LpWin]::Title($fg), ($fg -eq $h))
    }
}
