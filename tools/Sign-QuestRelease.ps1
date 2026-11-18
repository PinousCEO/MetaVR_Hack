# Sign the exported headset build. Keep this Windows user's signing directory backed up.
param(
    [string]$Apk = "$PSScriptRoot\..\dist\Yonder\LivingAIPartner-Quest\LivingAIPartner.apk",
    [string]$Output = "$PSScriptRoot\..\dist\Yonder\Release\Yonder-Quest.apk"
)
$ErrorActionPreference = 'Stop'
$unityAndroid = Get-ChildItem "$env:ProgramFiles\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer" -Directory | Sort-Object FullName | Select-Object -Last 1
$jdk = Join-Path $unityAndroid.FullName 'OpenJDK\bin'
$sdk = Get-ChildItem (Join-Path $unityAndroid.FullName 'SDK\build-tools') -Directory | Sort-Object Name | Select-Object -Last 1
$signer = Join-Path $sdk.FullName 'lib\apksigner.jar'
$keyDir = Join-Path $env:LOCALAPPDATA 'YonderSigning'
$null = New-Item -ItemType Directory -Force $keyDir
$keyFolder = Get-Item -LiteralPath $keyDir
$acl = $keyFolder.GetAccessControl([Security.AccessControl.AccessControlSections]::Access)
$acl.SetAccessRuleProtection($true,$false)
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
$rule = [Security.AccessControl.FileSystemAccessRule]::new($owner,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
$acl.SetAccessRule($rule)
$keyFolder.SetAccessControl($acl)
$store = Join-Path $keyDir 'yonder-release.p12'
$secretFile = Join-Path $keyDir 'password.dpapi'
Add-Type -AssemblyName System.Security
if ((Test-Path $store) -xor (Test-Path $secretFile)) { throw 'Signing identity is incomplete. Restore the existing keystore and password before continuing.' }
if (!(Test-Path $secretFile)) {
    $entropy = New-Object byte[] 36
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($entropy) } finally { $rng.Dispose() }
    $protected = [Security.Cryptography.ProtectedData]::Protect($entropy,$null,'CurrentUser')
    [IO.File]::WriteAllBytes($secretFile,$protected)
}
$bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($secretFile),$null,'CurrentUser')
$env:YONDER_KEYSTORE_PASS = [Convert]::ToBase64String($bytes)
$target = [IO.Path]::GetFullPath($Output)
$null = New-Item -ItemType Directory -Force (Split-Path -Parent $target)
$staged = Join-Path (Split-Path -Parent $target) (([guid]::NewGuid()).ToString() + '.apk')
try {
    if (!(Test-Path $store)) {
        & "$jdk\keytool.exe" -genkeypair -keystore $store -storetype PKCS12 -storepass:env YONDER_KEYSTORE_PASS -keypass:env YONDER_KEYSTORE_PASS -alias yonder -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Yonder Release'
        if ($LASTEXITCODE) { throw 'Key generation failed.' }
    }
    & "$jdk\java.exe" -jar $signer sign --ks $store --ks-key-alias yonder --ks-pass env:YONDER_KEYSTORE_PASS --min-sdk-version 24 --v2-signing-enabled true --v3-signing-enabled true --v4-signing-enabled false --out $staged $Apk
    if ($LASTEXITCODE) { throw 'Signing failed.' }
    & "$jdk\java.exe" -jar $signer verify --verbose --print-certs $staged
    if ($LASTEXITCODE) { throw 'APK signature check failed.' }
    & "$jdk\java.exe" -jar $signer verify --min-sdk-version 24 --max-sdk-version 27 $staged
    if ($LASTEXITCODE) { throw 'APK v2 signature check failed.' }
    & "$($sdk.FullName)\zipalign.exe" -c 4 $staged
    if ($LASTEXITCODE) { throw 'APK alignment check failed.' }
    Move-Item -LiteralPath $staged -Destination $target -Force
} finally {
    Remove-Item Env:\YONDER_KEYSTORE_PASS -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged }
}
