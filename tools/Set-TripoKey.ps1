# Stores the Tripo API key the Companion Host uses to make 3D models from a headset photo or description.
# The key is encrypted for the current Windows user (DPAPI) and written to %LOCALAPPDATA%\LivingAIPartner\tripo.key.dpapi.
# It is never written to the repository or to a log. The host checks for the key on the next creation request.
# (The host also accepts the key from the environment variables LP_TRIPO_KEY or TRIPO_API_KEY; those win over the file.)
#   .\tools\Set-TripoKey.ps1            asks for the key (input is hidden)
#   .\tools\Set-TripoKey.ps1 -Remove    deletes the stored key
#   .\tools\Set-TripoKey.ps1 -Check     says whether a key is stored and whether Tripo accepts it (the key is not shown)
param([switch]$Remove, [switch]$Check)
Add-Type -AssemblyName System.Security
$dir = Join-Path $env:LOCALAPPDATA "LivingAIPartner"
$file = Join-Path $dir "tripo.key.dpapi"
if ($Remove) { if (Test-Path $file) { Remove-Item $file -Force; "stored key removed" } else { "no key was stored" }; exit 0 }
if ($Check) {
    if (-not (Test-Path $file)) { "no key is stored"; exit 1 }
    $key = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($file), $null, 'CurrentUser'))
    # the balance of the account: the cheapest call there is, it costs no credit
    try { $r = Invoke-RestMethod -Uri "https://openapi.tripo3d.ai/v3/account/balance" -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 20 }
    catch { "a key is stored but Tripo refused it: " + $_.Exception.Message; exit 2 }
    # Tripo answers { code: 0, data: { balance, frozen } }; any other code is a refusal even with HTTP 200
    if ($r.code -ne 0) { "a key is stored but Tripo refused it: code $($r.code) $($r.message)"; exit 2 }
    "a key is stored and Tripo accepts it (balance $($r.data.balance) credits, $($r.data.frozen) held for running tasks)"; exit 0
}
$secure = Read-Host "Tripo API key" -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
if ([string]::IsNullOrWhiteSpace($key)) { "nothing entered; nothing changed"; exit 1 }
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllBytes($file, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($key.Trim()), $null, 'CurrentUser'))
"key stored for this Windows user. Ready for the next 3D creation request."
