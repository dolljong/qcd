@echo off
rem qcd - quick cd. Wrapper for qcd.exe.

for /f "tokens=2 delims=:" %%a in ('chcp') do set "_QCD_OLDCP=%%a"
set "_QCD_OLDCP=%_QCD_OLDCP: =%"
chcp 65001 >nul

set "_QCD_TMP=%TEMP%\qcd_target_%RANDOM%%RANDOM%.txt"
"%~dp0qcd-core.exe" --out "%_QCD_TMP%" %*
set "_QCD_TARGET="
if exist "%_QCD_TMP%" (
    set /p _QCD_TARGET=<"%_QCD_TMP%"
    del "%_QCD_TMP%" >nul 2>&1
)

chcp %_QCD_OLDCP% >nul

if defined _QCD_TARGET (
    cd /d "%_QCD_TARGET%"
)

set "_QCD_TARGET="
set "_QCD_TMP="
set "_QCD_OLDCP="
