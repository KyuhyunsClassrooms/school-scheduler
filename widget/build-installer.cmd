@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -File "%~dp0build-installer.ps1" %*
exit /b %errorlevel%
