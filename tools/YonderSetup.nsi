Unicode true
RequestExecutionLevel user
Name "Yonder Companion"
OutFile @OUTPUT@
InstallDir "$LOCALAPPDATA\Programs\Yonder"
SetCompressor zlib
ShowInstDetails nevershow
!include "MUI2.nsh"
!include "x64.nsh"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!define AppKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\YonderCompanion"
Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Yonder requires 64-bit Windows."
    Abort
  ${EndIf}
FunctionEnd
Section "Yonder Companion"
  SetShellVarContext current
@INSTALL@
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortcut "$SMPROGRAMS\Yonder Companion.lnk" "$INSTDIR\Start-Yonder.cmd" "" "$INSTDIR\LivingAIPartner-Windows\LivingAIPartner.exe"
  WriteRegStr HKCU "${AppKey}" "DisplayName" "Yonder Companion"
  WriteRegStr HKCU "${AppKey}" "DisplayIcon" "$INSTDIR\LivingAIPartner-Windows\LivingAIPartner.exe"
  WriteRegStr HKCU "${AppKey}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${AppKey}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${AppKey}" "NoModify" 1
  WriteRegDWORD HKCU "${AppKey}" "NoRepair" 1
SectionEnd
Section "Uninstall"
  SetShellVarContext current
@UNINSTALL@
  Delete "$SMPROGRAMS\Yonder Companion.lnk"
  DeleteRegKey HKCU "${AppKey}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
