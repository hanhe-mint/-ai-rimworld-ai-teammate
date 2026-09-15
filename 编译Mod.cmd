@echo off
chcp 65001 >nul
echo 请在提示后输入包含 RimWorldWin64.exe 的游戏目录。
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Source\AICoopCompanion\build.ps1"
pause
