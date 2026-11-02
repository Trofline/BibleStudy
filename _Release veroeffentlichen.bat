@echo off
cd /d "%~dp0"
echo Baut das Release und veroeffentlicht es auf GitHub (fragt vorher nach).
echo Zum Ueben ohne Veroeffentlichen: publish-release.ps1 -DryRun
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-release.ps1" %*
echo.
pause
