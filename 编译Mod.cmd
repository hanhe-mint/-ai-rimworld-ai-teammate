@echo off
chcp 65001 >nul
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Source\AICoopCompanion\build.ps1"
if errorlevel 1 echo Build failed. See the message above.
pause
