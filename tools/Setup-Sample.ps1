# Prepares the reviewer sample project: copies samples\sample-site to a working folder with its own git history,
# creates a LOCAL bare repository as its "origin" (so the approved-publish step can be verified without any GitHub
# account or personal token), registers it with the running Companion Host, and optionally opens it in an app window.
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA "LivingAIPartner\sample\sample-site"),
    [switch]$Open,
    [int]$X = 120, [int]$Y = 80, [int]$W = 1180, [int]$H = 760,
    [switch]$Reset
)
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "samples\sample-site"
$bare = Join-Path (Split-Path -Parent $Target) "sample-site-origin.git"
if ($Reset -and (Test-Path $Target)) { Remove-Item -Recurse -Force $Target; if (Test-Path $bare) { Remove-Item -Recurse -Force $bare } }
if (-not (Test-Path (Join-Path $Target ".git"))) {
    New-Item -ItemType Directory -Force $Target | Out-Null
    Copy-Item -Path (Join-Path $src "*") -Destination $Target -Recurse -Force
    git -C $Target init -q -b main
    git -C $Target -c user.name="Sample" -c user.email="sample@localhost" add -A
    git -C $Target -c user.name="Sample" -c user.email="sample@localhost" commit -q -m "Sample dashboard: initial version"
    git init -q --bare $bare
    git -C $Target remote add origin $bare
    git -C $Target push -q origin main
    Write-Output "sample project created at $Target (origin = local bare repo $bare)"
} else { Write-Output "sample project already exists at $Target" }
$ctl = Join-Path $root "Companion\LivingPartner.Ctl\bin\Release\net8.0\lpctl.exe"
if (-not (Test-Path $ctl)) { $ctl = Join-Path $root "dist\LivingAIPartner-Host\lpctl.exe" }
if (-not (Test-Path $ctl)) { $ctl = Join-Path $root "LivingAIPartner-Host\lpctl.exe" }   # when run from the dist folder
& $ctl allow-project $Target "sample-site" "Momo Notes" "index.html" 1 node test.js
Write-Output "registered with the host: window title contains 'Momo Notes', tests = 'node test.js', publish allowed to the local origin only"
if ($Open) {
    $edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
    $url = "file:///" + ((Join-Path $Target "index.html") -replace "\\", "/")
    $edgeProfile = Join-Path (Split-Path -Parent $Target) "edge-profile"
    if (Test-Path $edge) {
        # a dedicated app window with its own profile: the user's own browser session is not touched
        Start-Process $edge -ArgumentList "--app=$url", "--window-position=$X,$Y", "--window-size=$W,$H", "--user-data-dir=`"$edgeProfile`"", "--no-first-run", "--disable-features=msEdgeWelcomePage,CalculateNativeWinOcclusion", "--disable-backgrounding-occluded-windows", "--disable-renderer-backgrounding"   # keep painting while the window is in the background (it is a demo target, usually not focused)
    } else { Start-Process $url }
    Write-Output "opened $url"
}


