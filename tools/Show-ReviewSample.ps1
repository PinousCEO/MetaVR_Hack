# Shows a picture in a plain window that does not take the keyboard focus: something known for the partner to look at
# in the review checks. The window title can name a file ("meeting-notes.pdf - Sample Viewer") to test finding a file
# from a window title. Close it with -Close. (It keeps running until closed: start it with Start-Process.)
param([string]$Image = "samples\review\signup-form.png", [string]$Title = "signup-form.png - Sample Viewer", [int]$X = 980, [int]$Y = -1010, [int]$W = 900, [int]$H = 620, [switch]$Close)
if ($Close) {
    Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*Sample Viewer" } | ForEach-Object { try { $_ | Stop-Process -Force } catch { } }
    Write-Output "sample viewer closed"; exit 0
}
$root = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($Image)) { $Image = Join-Path $root $Image }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System.Windows.Forms;
public class LpSampleViewer : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }   // WS_EX_NOACTIVATE
}
'@
$f = New-Object LpSampleViewer
$f.Text = $Title
$f.StartPosition = 'Manual'
$f.Location = New-Object System.Drawing.Point($X, $Y)
$f.ClientSize = New-Object System.Drawing.Size($W, $H)
$f.BackColor = [System.Drawing.Color]::FromArgb(238, 238, 240)
$pb = New-Object System.Windows.Forms.PictureBox
$pb.Dock = 'Fill'; $pb.SizeMode = 'Zoom'
$bytes = [IO.File]::ReadAllBytes($Image)
$pb.Image = [System.Drawing.Image]::FromStream((New-Object IO.MemoryStream(, $bytes)))
$f.Controls.Add($pb)
[System.Windows.Forms.Application]::Run($f)
