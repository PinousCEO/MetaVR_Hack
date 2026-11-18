# Stores the OpenAI API key the Companion Host uses for conversation and voice (speech to text, replies, text to speech).
# The key is encrypted for the current Windows user (DPAPI) and written to %LOCALAPPDATA%\LivingAIPartner\openai.key.dpapi.
# It is never written to the repository or to a log. Restart the host afterwards.
#   .\tools\Set-OpenAiKey.ps1            asks for the key (input is hidden)
#   .\tools\Set-OpenAiKey.ps1 -Remove    deletes the stored key
#   .\tools\Set-OpenAiKey.ps1 -Check     says whether a key is stored and whether OpenAI accepts it (the key is not shown)
param([switch]$Remove, [switch]$Check)
Add-Type -AssemblyName System.Security
$dir = Join-Path $env:LOCALAPPDATA "LivingAIPartner"
$file = Join-Path $dir "openai.key.dpapi"
if ($Remove) { if (Test-Path $file) { Remove-Item $file -Force; "stored key removed" } else { "no key was stored" }; exit 0 }
if ($Check) {
    if (-not (Test-Path $file)) { "no key is stored"; exit 1 }
    $key = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($file), $null, 'CurrentUser'))
    try { $r = Invoke-RestMethod -Uri "https://api.openai.com/v1/models" -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 20; "a key is stored and OpenAI accepts it ($($r.data.Count) models visible)"; exit 0 }
    catch { "a key is stored but OpenAI refused it: " + $_.Exception.Message; exit 2 }
}
$secure = Read-Host "OpenAI API key" -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
if ([string]::IsNullOrWhiteSpace($key)) { "nothing entered; nothing changed"; exit 1 }
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllBytes($file, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($key.Trim()), $null, 'CurrentUser'))
"key stored for this Windows user. Restart the Companion Host to use it."
