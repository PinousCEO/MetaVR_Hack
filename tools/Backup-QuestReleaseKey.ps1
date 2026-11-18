# Export the signing identity with a new password you choose at the keytool prompt.
param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $out) { throw 'Choose a new file for the encrypted backup.' }
$keyDir=Join-Path $env:LOCALAPPDATA 'YonderSigning'
$store=Join-Path $keyDir 'yonder-release.p12'
$secretFile=Join-Path $keyDir 'password.dpapi'
$keytool=Get-ChildItem "$env:ProgramFiles\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe" | Select-Object -Last 1
Add-Type -AssemblyName System.Security
$bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($secretFile),$null,'CurrentUser')
$env:YONDER_KEYSTORE_PASS=[Convert]::ToBase64String($bytes)
try {
    & $keytool.FullName -importkeystore -srckeystore $store -srcstoretype PKCS12 -srcstorepass:env YONDER_KEYSTORE_PASS -srcalias yonder -destkeystore $out -deststoretype PKCS12
    if ($LASTEXITCODE) {throw 'Backup failed.'}
} finally { Remove-Item Env:\YONDER_KEYSTORE_PASS -ErrorAction SilentlyContinue }
