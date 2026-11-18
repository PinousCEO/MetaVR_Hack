# Makes the two sample files the review / read checks use, with flaws put in on purpose so that the feedback can be
# checked against something known:
#   samples\review\signup-form.png   - a sign-up screen with typos ("Creat your acount", "Sumbit"), pale grey text on
#                                      grey, two buttons of different size that do not line up, tiny legal text
#   samples\review\meeting-notes.pdf - a one-page note with fixed facts (budget 4,200,000 KRW, launch 2026-12-03),
#                                      typos and one contradiction (the launch date is given twice, differently)
# They are also copied to Documents\LivingAIPartner\Samples so that "the file named in a window title" can be found.
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "samples\review"
New-Item -ItemType Directory -Force $dir | Out-Null
Add-Type -AssemblyName System.Drawing

# ---- the sign-up screen
$w = 1100; $h = 720
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::FromArgb(238, 238, 240))
function Brush($r, $gr, $b) { New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($r, $gr, $b)) }
function Font($size, $style = 'Regular') { New-Object System.Drawing.Font("Segoe UI", $size, [System.Drawing.FontStyle]::$style, [System.Drawing.GraphicsUnit]::Pixel) }
$g.FillRectangle((Brush 255 255 255), 250, 60, 600, 600)
$g.DrawString("Creat your acount", (Font 40 Bold), (Brush 40 40 48), 300, 100)
$g.DrawString("Join Momo Notes today", (Font 18), (Brush 200 200 205), 302, 160)          # far too pale
$labels = @("Email adress", "Password", "Confirm pasword")
for ($i = 0; $i -lt 3; $i++) {
    $y = 220 + $i * 92
    $g.DrawString($labels[$i], (Font 15), (Brush 190 190 196), 302, $y)                   # pale label
    $g.FillRectangle((Brush 244 244 246), 300, ($y + 26), 500, 44)
    $g.DrawString("type here", (Font 16), (Brush 226 226 230), 314, ($y + 36))            # nearly invisible placeholder
}
$g.FillRectangle((Brush 214 40 40), 300, 520, 150, 38)                                    # small red primary button
$g.DrawString("Sumbit", (Font 17 Bold), (Brush 255 255 255), 340, 528)
$g.FillRectangle((Brush 120 120 128), 520, 506, 280, 66)                                  # larger secondary button, not aligned
$g.DrawString("Cancel", (Font 24 Bold), (Brush 255 255 255), 616, 522)
$g.DrawString("By continuing you agree to the Terms of Service and the Privacy Policy and to receive marketing email.", (Font 9), (Brush 170 170 176), 300, 610)
$g.Dispose()
$png = Join-Path $dir "signup-form.png"
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

# ---- the meeting note (a real text PDF, printed by Edge)
$html = @'
<!doctype html><html><head><meta charset="utf-8"><style>
body { font-family: "Segoe UI", sans-serif; margin: 48px 56px; color: #222; line-height: 1.5; }
h1 { font-size: 26px; } h2 { font-size: 17px; margin-top: 22px; } p, li { font-size: 13.5px; }
</style></head><body>
<h1>Momo Notes - launch planing meeting</h1>
<p>Date: 2026-11-10 &nbsp; Attendees: Jiho Park (product), Mina Seo (design), Daniel Cho (engineering)</p>
<h2>Decisions</h2>
<ul>
<li>The launch date is set to 2026-12-03.</li>
<li>The total budget is 4,200,000 KRW, of wich 1,500,000 KRW is for marketing.</li>
<li>The onboarding flow will be cut from five screens to three.</li>
<li>Daniel will finish the sync engine by 2026-11-24.</li>
</ul>
<h2>Open questions</h2>
<ul>
<li>Do we ship the Android app at the same time? No decision yet.</li>
<li>Who owns customer support in the first week?</li>
</ul>
<h2>Next steps</h2>
<p>Mina sends the new onboarding designs by friday. We meet again one week before the launch on 2026-12-10 to do the final check, becuase the launch is importent and we want to be sure that everything is being tested very very carefully and nothing is forgot.</p>
</body></html>
'@
$htmlPath = Join-Path $dir "meeting-notes.html"
Set-Content -Path $htmlPath -Value $html -Encoding utf8
$pdf = Join-Path $dir "meeting-notes.pdf"
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($edge) {
    Remove-Item $pdf -ErrorAction SilentlyContinue
    $tmp = Join-Path $env:TEMP "lp-edge-print"
    Start-Process -FilePath $edge -ArgumentList "--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--user-data-dir=`"$tmp`"", "--print-to-pdf=`"$pdf`"", "`"file:///$($htmlPath -replace '\\','/')`"" -Wait -WindowStyle Hidden
}
if (-not (Test-Path $pdf)) { Write-Warning "meeting-notes.pdf was not made (Microsoft Edge not found?)" }

$docs = Join-Path ([Environment]::GetFolderPath('MyDocuments')) "LivingAIPartner\Samples"
New-Item -ItemType Directory -Force $docs | Out-Null
Copy-Item $png, $pdf -Destination $docs -Force -ErrorAction SilentlyContinue
Get-ChildItem $dir | Select-Object Name, Length | Format-Table -AutoSize
"copied to $docs"
