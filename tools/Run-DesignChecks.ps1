# Sets every character design in the list (there is one: the owner's own model, "momo") and checks that the host state,
# the Windows client and the MR client all show the SAME look (each client reports the design it is actually rendering).
# Also checks the "next design" command and that an unknown design is refused.
# The MR client here is the desktop simulator - these are simulator results, never Quest evidence.
# Needs: tools\Start-Dev.ps1 -Sim ... running.
param([string]$Out = "artifacts\design-checks.txt", [switch]$Shots)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
$ids = @("momo")

function Looks {
    $s = & $ctl status
    $h = ""; $pc = ""; $mr = ""
    if (($s | Select-Object -First 1) -match "design=(\w*)") { $h = $Matches[1] }
    $l = $s | Select-String "^\s*pc:"; if ($l -and $l.Line -match "design=(\w*)") { $pc = $Matches[1] }
    $l = $s | Select-String "^\s*mr:"; if ($l -and $l.Line -match "design=(\w*)") { $mr = $Matches[1] }
    return @($h, $pc, $mr)
}

for ($i = 0; $i -lt 30 -and -not (& $ctl status | Select-String "^\s*mr:"); $i++) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 2
$res = @("Design checks $(Get-Date -Format s)  (MR client: desktop simulator - NOT a Quest)")
$ok = $true

$res += "--- set each design: host state / Windows client / MR client must agree"
foreach ($id in $ids) {
    & $ctl debug setDesign 0 0 0 $id | Out-Null
    Start-Sleep -Milliseconds 1500
    $l = Looks
    $pass = ($l[0] -eq $id) -and ($l[1] -eq $id) -and ($l[2] -eq $id)
    if (-not $pass) { $ok = $false }
    $res += ("{0,-8} host={1,-8} pc={2,-8} mr={3,-8} {4}" -f $id, $l[0], $l[1], $l[2], $(if ($pass) { "PASS" } else { "FAIL" }))
    if ($Shots) {
        $p = Join-Path $root "artifacts\design-$id-mr.png"
        & $ctl debug simShot 0 0 0 $p | Out-Null
        Start-Sleep -Milliseconds 900
    }
}

$res += "--- next design: one full cycle"
& $ctl debug setDesign 0 0 0 momo | Out-Null; Start-Sleep -Milliseconds 800
$seq = @()
for ($i = 0; $i -lt $ids.Count; $i++) { & $ctl debug nextDesign | Out-Null; Start-Sleep -Milliseconds 1200; $l = Looks; $seq += $l[0]; if (($l[1] -ne $l[0]) -or ($l[2] -ne $l[0])) { $ok = $false } }
$expect = (@($ids | Select-Object -Skip 1) + $ids[0]) -join ","
$cyc = (($seq -join ",") -eq $expect)
if (-not $cyc) { $ok = $false }
$res += "cycle: $($seq -join ',')  (expect $expect)  $(if ($cyc) { 'PASS' } else { 'FAIL' })"

$res += "--- an unknown design is refused"
& $ctl debug setDesign 0 0 0 dragon | Out-Null; Start-Sleep -Milliseconds 1000
$l = Looks
$ref = ($l[0] -eq "momo") -and ($l[1] -eq "momo") -and ($l[2] -eq "momo")
if (-not $ref) { $ok = $false }
$res += "after 'dragon': host=$($l[0]) pc=$($l[1]) mr=$($l[2])  $(if ($ref) { 'PASS' } else { 'FAIL' })"

$res += ""
$res += "RESULT: $(if ($ok) { 'PASS' } else { 'FAIL' })"
$res | Set-Content -Path (Join-Path $root $Out) -Encoding utf8
$res
