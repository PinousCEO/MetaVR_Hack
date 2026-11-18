# Voice conversation, end to end, against a running tools\Start-Dev.ps1 -Sim set-up with an OpenAI key stored
# (tools\Set-OpenAiKey.ps1). The "user's voice" is a recording from samples\voice fed in by the MR client through the
# same path a microphone recording takes (labelled test-file in the host log); everything after it is real: speech to
# text, the reply and its action, text to speech, playback by the client on the side the partner is on.
# One step opens the REAL microphone for a moment (a scripted pinch on the partner) and then throws the recording away.
# The MR client here is the desktop simulator - these are simulator results, never Quest evidence.
param([string]$Out = "artifacts\voice-checks.txt", [string]$Monitor = "DISPLAY3", [switch]$Shots)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$logs = Join-Path $env:LOCALAPPDATA "LivingAIPartner\logs"
$hostLog = Join-Path $logs "host.log"; $simLog = Join-Path $logs "mr-sim.log"; $pcLog = Join-Path $logs "pc-client.log"

function Head { (& $ctl status | Select-Object -First 1) }
function Talk { if ((Head) -match "talk=(\S+)") { $Matches[1] } else { "?" } }
function HostSince([int]$from) { @(Get-Content $hostLog -Encoding UTF8 | Select-Object -Skip $from) }
function Lines([string]$path) { if (Test-Path $path) { @(Get-Content $path -Encoding UTF8).Count } else { 0 } }
function WaitQuiet([int]$seconds) {
    # until the partner has finished thinking and speaking
    $seen = $false
    for ($i = 0; $i -lt $seconds * 4; $i++) { Start-Sleep -Milliseconds 250; $t = Talk; if ($t -ne "-") { $seen = $true } elseif ($seen) { return $true } }
    return $false
}
function Say([string[]]$log) { ($log | Where-Object { $_ -match "say\[" } | Select-Object -Last 1) -replace '^.*?say\[', 'say[' }
function Heard([string[]]$log) { if (($log | Where-Object { $_ -match "transcribed in (\d+) ms: '(.*)'" } | Select-Object -Last 1) -match "transcribed in (\d+) ms: '(.*)'") { "$($Matches[2])  [$($Matches[1]) ms]" } else { "" } }
function Shot([string]$name) { if ($Shots) { & $ctl debug simShot 0 0 0 (Join-Path $root "artifacts\$name") | Out-Null } }

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 2
$res = @("Voice checks $(Get-Date -Format s)  (MR client: desktop simulator - NOT a Quest; the user's voice: recordings from samples\voice, labelled test-file)")
$ok = $true
$res += "start: " + (Head)
if (-not ((Invoke-RestMethod "http://127.0.0.1:47610/status").conversation.enabled)) { $res += "FAIL: the host has no OpenAI key (tools\Set-OpenAiKey.ps1)"; $res | Set-Content (Join-Path $root $Out) -Encoding utf8; $res; exit 1 }

# ---- V1 a spoken command in English: timer
$res += ""; $res += "--- V1 spoken command (English): '" + (Get-Content samples\voice\en-timer.txt -Encoding UTF8) + "'"
$h0 = Lines $hostLog; $s0 = Lines $simLog
& $ctl voicefile samples\voice\en-timer.wav | Out-Null
$done = WaitQuiet 25
$log = HostSince $h0
$timer = @(& $ctl status | Select-String "module .* timer .* status=running").Count -gt 0
$played = @(Get-Content $simLog -Encoding UTF8 | Select-Object -Skip $s0 | Where-Object { $_ -match "voice: playing" }).Count -gt 0
$res += "heard:  " + (Heard $log); $res += "reply:  " + (Say $log)
$res += "timer running=$timer  spoken reply played by the MR client=$played  finished=$done"
$p = $timer -and $played -and $done -and ((Heard $log) -match "timer"); if (-not $p) { $ok = $false }
$res += "V1: $(if ($p) { 'PASS' } else { 'FAIL' })"

# ---- V2 small talk in Korean: answered in Korean, no action
$res += ""; $res += "--- V2 small talk (Korean): '" + (Get-Content samples\voice\ko-chat.txt -Encoding UTF8) + "'"
$h0 = Lines $hostLog
& $ctl voicefile samples\voice\ko-chat.wav | Out-Null
Start-Sleep -Milliseconds 4200; Shot "voice-speaking-mr.png"
$done = WaitQuiet 25
$log = HostSince $h0
$reply = Say $log
$korean = $reply -match "\p{IsHangulSyllables}"
$noAction = @($log | Where-Object { $_ -match "conversation: reply .* action=none" }).Count -gt 0
$res += "heard:  " + (Heard $log); $res += "reply:  " + $reply
$res += "answered in Korean=$korean  no action taken=$noAction"
$p = $korean -and $noAction -and $done; if (-not $p) { $ok = $false }
$res += "V2: $(if ($p) { 'PASS' } else { 'FAIL' })"

# ---- V3 speaking while pointing: "go into the screen over there" goes in at the pointed spot
$res += ""; $res += "--- V3 speaking while pointing (Korean): '" + (Get-Content samples\voice\ko-enter.txt -Encoding UTF8) + "'"
$u = 0.72; $v = 0.46
$b = (& $ctl status | Select-String "$Monitor bounds=\[(-?\d+),(-?\d+) (\d+)x(\d+)\]").Matches[0].Groups
$wantX = [double]$b[1].Value + $u * [double]$b[3].Value; $wantY = [double]$b[2].Value + $v * [double]$b[4].Value
$h0 = Lines $hostLog
& $ctl debug simPoint 9 $u $v $Monitor | Out-Null          # the scripted hand points at that spot for 9 s
Start-Sleep -Milliseconds 1500
& $ctl voicefile samples\voice\ko-enter.wav | Out-Null
Start-Sleep -Seconds 6
& $ctl wait 60 | Out-Null
$raw = (& $ctl snapshot) -join "`n"
$log = HostSince $h0
$tx = 0; $ty = 0
if ($raw -match '(?s)"target":\s*\{.*?"pxX":\s*(-?[\d.]+),\s*"pxY":\s*(-?[\d.]+)') { $tx = [double]$Matches[1]; $ty = [double]$Matches[2] }
$owner3 = if ((Head) -match "owner=(\w+)") { $Matches[1] } else { "?" }
$err = [math]::Sqrt([math]::Pow($tx - $wantX, 2) + [math]::Pow($ty - $wantY, 2))
$res += "heard:  " + (Heard $log); $res += "reply:  " + (Say $log)
$res += ("pointed at pixel ({0:0},{1:0}) on {2}; the partner went in at ({3:0},{4:0}); difference {5:0.0} px; owner={6}" -f $wantX, $wantY, $Monitor, $tx, $ty, $err, $owner3)
$p = ($owner3 -eq "pc") -and ($err -lt 12); if (-not $p) { $ok = $false }
$res += "V3: $(if ($p) { 'PASS' } else { 'FAIL' })"
WaitQuiet 12 | Out-Null

# ---- V4 spoken while it is inside the PC: come back; the reply is played by the PC client
$res += ""; $res += "--- V4 spoken while the partner is in the PC (English): '" + (Get-Content samples\voice\en-comeback.txt -Encoding UTF8) + "'"
$h0 = Lines $hostLog; $p0 = Lines $pcLog
& $ctl voicefile samples\voice\en-comeback.wav | Out-Null
Start-Sleep -Seconds 6
& $ctl wait 60 | Out-Null
$log = HostSince $h0
$pcPlayed = @(Get-Content $pcLog -Encoding UTF8 | Select-Object -Skip $p0 | Where-Object { $_ -match "voice: playing" }).Count -gt 0
$owner = if ((Head) -match "owner=(\w+)") { $Matches[1] } else { "?" }
$res += "heard:  " + (Heard $log); $res += "reply:  " + (Say $log)
$res += "the PC client played the spoken reply=$pcPlayed  owner afterwards=$owner"
$p = $pcPlayed -and ($owner -eq "mr"); if (-not $p) { $ok = $false }
$res += "V4: $(if ($p) { 'PASS' } else { 'FAIL' })"
WaitQuiet 12 | Out-Null
Start-Sleep -Seconds 4                                        # let it walk back to its place

# ---- V5 a pinch on the partner opens the real microphone; the recording is thrown away
$res += ""; $res += "--- V5 pinch on the partner (scripted hand): the real microphone opens; cancelled, nothing is sent"
$h0 = Lines $hostLog
& $ctl debug simPinchPartner | Out-Null
$listening = $false
for ($i = 0; $i -lt 16 -and -not $listening; $i++) { Start-Sleep -Milliseconds 250; if ((Talk) -eq "listening") { $listening = $true } }
Start-Sleep -Milliseconds 600; Shot "voice-listening-mr.png"; Start-Sleep -Milliseconds 400
& $ctl listencancel | Out-Null
Start-Sleep -Seconds 1
$after = Talk
$clips = @(HostSince $h0 | Where-Object { $_ -match "voice clip from" }).Count
$res += "talk state after the pinch=$(if ($listening) { 'listening' } else { 'NOT listening' })  after cancel=$after  recordings sent=$clips"
$p = $listening -and ($after -eq "-") -and ($clips -eq 0); if (-not $p) { $ok = $false }
$res += "V5: $(if ($p) { 'PASS' } else { 'FAIL' })"

$res += ""
$res += "RESULT: $(if ($ok) { 'PASS' } else { 'FAIL' })"
$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res
