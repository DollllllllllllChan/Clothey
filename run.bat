@echo off
taskkill /f /im Clothey.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

set "EXE_PATH=%~dp0publish\Clothey.exe"
if not exist "%EXE_PATH%" set "EXE_PATH=%~dp0src\PegSnap\bin\Release\net8.0-windows10.0.19041.0\Clothey.exe"

echo Starting Clothey...
start "" "%EXE_PATH%"
echo Clothey is running live in the background!
