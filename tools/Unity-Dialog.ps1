# Lists the modal dialogs of the Unity Editor (windows of class #32770 with their texts and buttons) and, when -Title
# and -Button are given, presses that button. The Editor answers nothing over MCP while such a dialog is open - a
# call then hangs for 30 minutes - so check with this before a long call:
#   .\tools\Unity-Dialog.ps1                                   # prints "none" or each dialog
#   .\tools\Unity-Dialog.ps1 -Title "Low disk space" -Button OK
param([string]$Title = "", [string]$Button = "")
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class LpUnityDialogs {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  public static List<string> Dialogs(uint pid, string clickTitle, string clickButton) {
    var r = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid || !IsWindowVisible(h)) return true;
      var c = new StringBuilder(100); GetClassName(h, c, 100);
      if (c.ToString() != "#32770") return true;
      var t = new StringBuilder(300); GetWindowText(h, t, 300);
      string texts = "";
      EnumChildWindows(h, (ch, l2) => {
        var t2 = new StringBuilder(600); GetWindowText(ch, t2, 600);
        var c2 = new StringBuilder(100); GetClassName(ch, c2, 100);
        if (t2.Length > 0) texts += " [" + c2 + ": " + t2 + "]";
        if (clickTitle.Length > 0 && t.ToString() == clickTitle && t2.ToString() == clickButton) { PostMessage(ch, 0x00F5, IntPtr.Zero, IntPtr.Zero); texts += " <pressed>"; }
        return true; }, IntPtr.Zero);
      r.Add("'" + t + "'" + texts);
      return true; }, IntPtr.Zero);
    return r; } }
"@ -ErrorAction SilentlyContinue
$found = $false
foreach ($u in @(Get-Process Unity -ErrorAction SilentlyContinue)) {
    foreach ($d in [LpUnityDialogs]::Dialogs([uint32]$u.Id, $Title, $Button)) { $found = $true; Write-Output ("dialog " + $d) }
}
if (-not $found) { Write-Output "none" }
