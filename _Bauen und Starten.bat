@echo off
cd /d "%~dp0"
echo Baue BibleStudy ...
dotnet build src\BibleStudy.App -c Debug --nologo -v q
if errorlevel 1 (
  echo.
  echo FEHLER beim Bauen. Laeuft BibleStudy noch? Dann bitte erst schliessen.
  pause
  exit /b 1
)
start "" "src\BibleStudy.App\bin\Debug\net10.0\BibleStudy.App.exe"
