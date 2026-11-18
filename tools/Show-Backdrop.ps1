# Shows a plain, non-activating window as a neutral background for captures and recordings of the PC character, so
# that evidence images do not contain whatever else is open on that display. Close it with -Close (or kill the process).
param([int]$X = 960, [int]$Y = -1040, [int]$W = 940, [int]$H = 1000, [string]$Color = "#F3EEE4", [switch]$Close)
$title = "Momo Backdrop"
if ($Close) {
    Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq $title } | ForEach-Object { try { $_ | Stop-Process -Force } catch { } }
    Write-Output "backdrop closed"; exit 0
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System.Windows.Forms;
public class LpBackdrop : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }   // WS_EX_NOACTIVATE
}
'@
$f = New-Object LpBackdrop
$f.Text = $title
$f.FormBorderStyle = 'None'
$f.StartPosition = 'Manual'
$f.Location = New-Object System.Drawing.Point($X, $Y)
$f.Size = New-Object System.Drawing.Size($W, $H)
$f.BackColor = [System.Drawing.ColorTranslator]::FromHtml($Color)
$f.ShowInTaskbar = $false
[System.Windows.Forms.Application]::Run($f)
