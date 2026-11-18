# End-to-end developer-work check on the SAMPLE project with the REAL harness (Claude Code CLI):
#   point at the sample window -> partner enters through the portal -> real analysis (screenshot + source) -> proposals
#   -> partner returns to MR -> one proposal is selected -> real code change on a dedicated branch/worktree
#   -> the project's tests run -> review -> explicitly approved push to the sample's LOCAL bare origin.
# Requires: tools\Start-Dev.ps1 -Sim (or a paired Quest), tools\Setup-Sample.ps1 -Open. Costs a small amount of harness usage.
param([string]$Title = "Momo Notes", [int]$Select = 0, [string]$Prompt = "Please look at the design of this page. The task cards feel cluttered", [string]$Out = "artifacts\harness-e2e.txt")
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$sample = Join-Path $env:LOCALAPPDATA "LivingAIPartner\sample\sample-site"
$bare = Join-Path $env:LOCALAPPDATA "LivingAIPartner\sample\sample-site-origin.git"
$log = @("Harness E2E $(Get-Date -Format s)  (real harness - which one is shown as harness=... on each task line; MR client may be the simulator - see status line)")
function Note($s) { $script:log += $s; Write-Output $s }
function W($action, $extra = "") { Invoke-Expression "powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action $action -Title '$Title' $extra" | Out-Null }

W restore; W topmost; Start-Sleep -Milliseconds 600
$r = powershell -NoProfile -ExecutionPolicy Bypass -File tools\WinCtl.ps1 -Action find -Title $Title
$m = [regex]::Match($r, "rect=\[(-?\d+),(-?\d+) (\d+)x(\d+)\]")
if (-not $m.Success) { Note "sample window not found: run tools\Setup-Sample.ps1 -Open"; $log | Set-Content (Join-Path $root $Out) -Encoding utf8; exit 1 }
$px = [int]$m.Groups[1].Value + [int]([int]$m.Groups[3].Value * 0.30); $py = [int]$m.Groups[2].Value + [int]([int]$m.Groups[4].Value * 0.30)
Note ("window: " + $r)
& $ctl play | Out-Null
if ((& $ctl status | Select-Object -First 1) -match "owner=pc") { & $ctl return 0 0 | Out-Null; & $ctl wait 30 | Out-Null }
Note ("status: " + ((& $ctl status | Select-String "mr:").Line.Trim()))

# 1. "go in here and look at the design" - pointed pixel is over the task cards
& $ctl arrival $Prompt | Out-Null
& $ctl enter $px $py | Out-Null
& $ctl wait 40 | Out-Null
Note ("after entering: " + (& $ctl status | Select-Object -First 1))
$t0 = Get-Date
& $ctl waittask "ProposalReady|TaskFailed|TaskCancelled" 420 | ForEach-Object { Note ("  " + $_) }
Note ("analysis took {0:0}s" -f ((Get-Date) - $t0).TotalSeconds)
& $ctl tasks | ForEach-Object { Note ("  " + $_) }
if (-not ((& $ctl tasks) -match "status=ProposalReady")) { Note "ANALYSIS DID NOT PRODUCE PROPOSALS - stopping"; $log | Set-Content (Join-Path $root $Out) -Encoding utf8; exit 2 }

# 2. the partner comes back out with the result; the MR client lays the proposals out as modules
& $ctl return 0 0 | Out-Null
& $ctl wait 40 | Out-Null
Note ("after returning: " + (& $ctl status | Select-Object -First 1))

# 3. choose one proposal -> real change + tests
& $ctl debug select $Select | Out-Null
$t0 = Get-Date
& $ctl waittask "ReviewRequired|TestFailed|TaskFailed|TaskCancelled" 720 | ForEach-Object { Note ("  " + $_) }
Note ("change + test took {0:0}s" -f ((Get-Date) - $t0).TotalSeconds)
& $ctl tasks | Select-String "task .*\[change\]|branch:|diff:|test:|events:" | ForEach-Object { Note ("  " + $_.Line.Trim()) }
$status = (& $ctl tasks | Select-String "\[change\]" | Select-Object -Last 1).Line
if ($status -notmatch "status=ReviewRequired") { Note "CHANGE DID NOT REACH ReviewRequired - nothing is published"; $log | Set-Content (Join-Path $root $Out) -Encoding utf8; exit 3 }

# 4. before approval nothing may be on the remote
$branch = ([regex]::Matches(((& $ctl tasks) -join "`n"), "branch: (partner/task-\w+)") | Select-Object -Last 1).Groups[1].Value   # the task that was just run
Note ("remote branches BEFORE approval: " + ((git -C $bare branch --list) -join " "))
# 5. explicit approval -> push of the task branch only
& $ctl task approvePublish | Out-Null
& $ctl waittask "PublishCompleted|TaskFailed" 120 | ForEach-Object { Note ("  " + $_) }
Note ("remote branches AFTER approval: " + ((git -C $bare branch --list) -join " "))
Note ("remote head of ${branch}: " + (git -C $bare log --oneline -1 $branch 2>&1))
Note ("default branch on remote untouched: " + (git -C $bare log --oneline -1 main 2>&1))
Note ("user's working copy untouched (still on main, clean): " + ((git -C $sample status --short --branch) -join " "))
Note ("files changed on the task branch: " + ((git -C $bare diff --stat main $branch) -join " | "))
$log | Set-Content (Join-Path $root $Out) -Encoding utf8
