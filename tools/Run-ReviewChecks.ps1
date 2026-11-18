# Looking at a screen and reading files, end to end against the real host, the real Windows client and the OpenAI API
# (the user's own key; this run costs a few cents: three text calls, one image). The headset is the desktop MR simulator.
#   R1 review   - a window is pointed at; the partner goes into THAT window, looks at it and gives feedback; on the PC it
#                 saves feedback.png (the capture with numbered marks), revised.png (redrawn with the changes) and review.pdf
#   R2 bring    - called back out, it brings the notes: the cards and the "saved" panel are shown in the room
#   R3 read     - a PDF given outright is read: notes.pdf and revised.pdf are saved, and the known facts and the
#                 contradiction planted in the sample are found
#   R4 ask      - a question about that file afterwards is answered from what was read
#   R5 title    - the file is found from the title of the window that shows it ("meeting-notes.pdf - Sample Viewer")
# The sample screen and the sample PDF have known flaws (tools\Make-ReviewSamples.ps1), so the feedback can be checked.
# Quiet mode is switched on for the run, so the replies are shown and logged but not spoken through the speakers.
# What this does NOT show: a real pointing hand on a Quest, and the notes as seen through a headset.
# Needs: tools\Start-Dev.ps1 -Sim ... running, an OpenAI key (tools\Set-OpenAiKey.ps1 -Check).
param([string]$Out = "artifacts\review-checks.txt", [int]$ViewerX = 980, [int]$ViewerY = -1010)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$log = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs\host.log"
function Head { (& $ctl status | Select-Object -First 1) }
function LastTask {
    $lines = @(& $ctl tasks); $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^task ") { $start = $i } }
    if ($start -lt 0) { return $null }
    $block = $lines[$start..($lines.Count - 1)] -join "`n"
    $o = [pscustomobject]@{ text = $block; id = ""; kind = ""; status = ""; title = ""; outputs = @(); notes = 0 }
    if ($block -match "^task (\S+) \[(\w+)\] status=(\w+)") { $o.id = $Matches[1]; $o.kind = $Matches[2]; $o.status = $Matches[3] }
    if ($block -match "(?m)^\s+title: (.*)$") { $o.title = $Matches[1].Trim() }
    if ($block -match "(?m)^\s+outputs: (.*)$") { $o.outputs = @($Matches[1].Split('|') | ForEach-Object { $_.Trim() }) }
    $o.notes = ([regex]::Matches($block, "(?m)^\s+module ")).Count
    $o
}
function WaitTask([string]$after, [int]$sec) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $sec) {
        $t = LastTask
        if ($t -and $t.id -ne $after -and $t.status -in @("ProposalReady", "TaskFailed", "TaskCancelled")) { return $t, [int]$sw.Elapsed.TotalSeconds }
        Start-Sleep -Seconds 2
    }
    return (LastTask), [int]$sw.Elapsed.TotalSeconds
}
function IsPdf($p) { if (-not (Test-Path $p)) { return $false }; $b = [IO.File]::ReadAllBytes($p); $b.Length -gt 20000 -and [Text.Encoding]::ASCII.GetString($b, 0, 5) -eq "%PDF-" }
function IsPng($p) { if (-not (Test-Path $p)) { return $false }; $b = [IO.File]::ReadAllBytes($p); $b.Length -gt 20000 -and $b[0] -eq 0x89 -and $b[1] -eq 0x50 -and $b[2] -eq 0x4E -and $b[3] -eq 0x47 }
function Sizes($files) { ($files | ForEach-Object { if (Test-Path $_) { "{0} {1:0} kB" -f (Split-Path -Leaf $_), ((Get-Item $_).Length / 1024) } else { (Split-Path -Leaf $_) + " MISSING" } }) -join ", " }
function Viewer([string]$title) {
    & (Join-Path $PSScriptRoot "Show-ReviewSample.ps1") -Close | Out-Null
    Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$(Join-Path $PSScriptRoot 'Show-ReviewSample.ps1')`"", "-Title", "`"$title`"", "-X", $ViewerX, "-Y", $ViewerY -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 3
}

for ($i = 0; $i -lt 30 -and -not ((& $ctl status) -join "`n" -match "(?m)^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 3
$res = @("Review / read checks $(Get-Date -Format s)  (pointing = the simulator's scripted selection, NOT a tracked hand on a Quest; the OpenAI calls are real)")
$ok = $true
& $ctl utter "Please switch quiet mode on." | Out-Null; Start-Sleep -Seconds 5
$res += "start: " + (Head)

# ---- R1
$res += ""; $res += "--- R1 point at a window: the partner goes into it, reviews it, saves feedback.png / revised.png / review.pdf"
Viewer "signup-form.png - Sample Viewer"
$px = $ViewerX + 450; $py = $ViewerY + 300
$before = LastTask; $beforeId = if ($before) { $before.id } else { "" }
& $ctl task analyzeOnArrival "" "" "Please look at this sign-up screen and give me feedback on the design." | Out-Null
& $ctl enter $px $py | Out-Null
$t, $sec = WaitTask $beforeId 240
$head = Head
$r1files = $false; $grounded = $false; $dir = ""
if ($t -and $t.status -eq "ProposalReady" -and $t.outputs.Count -ge 2) {
    $dir = Split-Path -Parent $t.outputs[0]
    $pdf = Join-Path $dir "review.pdf"; $rev = Join-Path $dir "revised.png"; $fb = Join-Path $dir "feedback.png"
    $r1files = (IsPdf $pdf) -and (IsPng $rev) -and (IsPng $fb)
    $json = if (Test-Path (Join-Path $dir "review.json")) { Get-Content (Join-Path $dir "review.json") -Raw } else { "" }
    $grounded = $json -match "(?i)contrast|Creat|acount|Sumbit|spelling|typo|adress|pasword"
    $res += "saved: " + (Sizes @($pdf, $rev, $fb)) + "  in $dir"
    Copy-Item $fb (Join-Path $root "artifacts\review-window-feedback.png") -Force; Copy-Item $rev (Join-Path $root "artifacts\review-window-revised.png") -Force; Copy-Item $pdf (Join-Path $root "artifacts\review-window.pdf") -Force
}
$res += "task: $(if ($t) { "$($t.kind) $($t.status) '$($t.title)' notes=$($t.notes) after $sec s" } else { 'none' })  | $head"
$res += ($t.text -split "`n" | Where-Object { $_ -match "^\s+module |^\s+proposal:" } | ForEach-Object { $_.Trim().Substring(0, [Math]::Min(190, $_.Trim().Length)) })
$res += "feedback names a flaw planted in the sample (contrast / typos): $grounded"
$p1 = $t -and $t.kind -eq "review" -and $t.status -eq "ProposalReady" -and $t.notes -ge 3 -and $r1files -and $grounded -and ($head -match "owner=pc") -and ($t.title -match "Sample Viewer")
$res += "R1: $(if ($p1) { 'PASS' } else { 'FAIL' })"; if (-not $p1) { $ok = $false }

# ---- R2
$res += ""; $res += "--- R2 called back out, it brings the notes into the room"
& $ctl return 0 0 | Out-Null; & $ctl wait 60 | Out-Null
Start-Sleep -Seconds 5
& $ctl debug simShot 0 0 0 (Join-Path $root "artifacts\review-mr-cards.png") | Out-Null
Start-Sleep -Seconds 2
$head = Head
$p2 = ($head -match "owner=mr") -and (Test-Path (Join-Path $root "artifacts\review-mr-cards.png"))
$res += "end: $head  | simulator view saved to artifacts\review-mr-cards.png (the cards and the panel are judged by looking at it)"
$res += "R2: $(if ($p2) { 'PASS' } else { 'FAIL' })"; if (-not $p2) { $ok = $false }
if ($t) { & $ctl task dismiss $t.id | Out-Null }

# ---- R3
$res += ""; $res += "--- R3 read a PDF: notes.pdf + revised.pdf, the facts and the planted contradiction"
$before = LastTask
& $ctl readfile (Join-Path $root "samples\review\meeting-notes.pdf") "Please read this file and tell me what it says." | Out-Null
$t3, $sec = WaitTask $before.id 180
$facts = $false; $contradiction = $false; $r3files = $false
if ($t3 -and $t3.status -eq "ProposalReady" -and $t3.outputs.Count -ge 1) {
    $dir3 = Split-Path -Parent $t3.outputs[0]
    $r3files = (IsPdf (Join-Path $dir3 "notes.pdf")) -and (IsPdf (Join-Path $dir3 "revised.pdf"))
    $json = Get-Content (Join-Path $dir3 "notes.json") -Raw
    $facts = ($json -match "4,200,000") -and ($json -match "2026-12-03")
    $contradiction = $json -match "2026-12-10"
    $res += "saved: " + (Sizes @((Join-Path $dir3 "notes.pdf"), (Join-Path $dir3 "revised.pdf"))) + "  in $dir3"
    Copy-Item (Join-Path $dir3 "notes.pdf") (Join-Path $root "artifacts\read-sample-notes.pdf") -Force; Copy-Item (Join-Path $dir3 "revised.pdf") (Join-Path $root "artifacts\read-sample-revised.pdf") -Force
}
$res += "task: $(if ($t3) { "$($t3.kind) $($t3.status) '$($t3.title)' suggestions=$($t3.notes) after $sec s" } else { 'none' })"
$res += "facts found (budget 4,200,000 KRW, launch 2026-12-03): $facts   the date that contradicts the launch (2026-12-10) is raised: $contradiction"
$p3 = $t3 -and $t3.kind -eq "read" -and $t3.status -eq "ProposalReady" -and $r3files -and $facts -and $contradiction
$res += "R3: $(if ($p3) { 'PASS' } else { 'FAIL' })"; if (-not $p3) { $ok = $false }

# ---- R4
$res += ""; $res += "--- R4 a question about the file afterwards"
$mark = (Get-Content $log).Count
& $ctl utter "In the file you just read, how much of the budget is for marketing?" | Out-Null
$reply = ""
for ($i = 0; $i -lt 20 -and -not $reply; $i++) { Start-Sleep -Seconds 1; $new = Get-Content $log | Select-Object -Skip $mark | Select-String "say\[" | Select-Object -Last 1; if ($new) { $reply = $new.Line -replace "^.*?say\[\w+\]: ", "" } }
$res += "reply: $reply"
$p4 = $reply -match "1,500,000|1\.5 ?million|150만|1500000"
$res += "R4: $(if ($p4) { 'PASS' } else { 'FAIL' })"; if (-not $p4) { $ok = $false }
if ($t3) { & $ctl task dismiss $t3.id | Out-Null }

# ---- R5
$res += ""; $res += "--- R5 the file is found from the title of the window that shows it"
Viewer "meeting-notes.pdf - Sample Viewer"
$before = LastTask
& $ctl readwindow $px $py "Please read the file in this window." | Out-Null
$t5, $sec = WaitTask $before.id 180
$found = ""
$m = Get-Content $log | Select-String "AnalysisStarted file=" | Select-Object -Last 1
if ($m) { $found = ($m.Line -replace "^.*AnalysisStarted ", "") }
$res += "task: $(if ($t5) { "$($t5.kind) $($t5.status) '$($t5.title)' after $sec s" } else { 'none' })"
$res += "how the file was found: $found"
$p5 = $t5 -and $t5.kind -eq "read" -and $t5.status -eq "ProposalReady" -and $t5.title -match "meeting-notes\.pdf" -and $found -match "named in the window title"
$res += "R5: $(if ($p5) { 'PASS' } else { 'FAIL' })"; if (-not $p5) { $ok = $false }
if ($t5) { & $ctl task dismiss $t5.id | Out-Null }

& (Join-Path $PSScriptRoot "Show-ReviewSample.ps1") -Close | Out-Null
& $ctl utter "Please switch quiet mode off." | Out-Null; Start-Sleep -Seconds 4
$res += ""
$res += "RESULT: $(if ($ok) { 'PASS' } else { 'FAIL' })"
$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res | Select-Object -Last 40
