@echo off
setlocal
set "gameHost=%~dp0LivingAIPartner-Host\LivingPartner.Host.exe"
if not exist "%gameHost%" set "gameHost=%~dp0LivingPartner.Host.exe"
if not exist "%gameHost%" set "gameHost=%~dp0dist\LivingAIPartner-Host\LivingPartner.Host.exe"
if not exist "%gameHost%" (
  echo The Demon Gate executable is missing. Extract the full Windows package and try again.
  pause
  exit /b 1
)
for %%I in ("%gameHost%") do set "soloPage=%%~dpIDemonGate\game.js"
findstr /L /C:"data-standalone" "%soloPage%" >nul 2>&1
if errorlevel 1 (
  echo This Windows package does not include the standalone game. Extract the updated package and try again.
  pause
  exit /b 1
)
start "" "%gameHost%" --demon-solo
