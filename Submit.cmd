@echo off
setlocal DisableDelayedExpansion
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Submit.ps1" -Pause
exit /b %errorlevel%
