@echo off
taskkill /f /im PegSnap.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

set "EXE_PATH=%~dp0publish\PegSnap.exe"
if not exist "%EXE_PATH%" set "EXE_PATH=%~dp0src\PegSnap\bin\Release\net8.0-windows10.0.19041.0\PegSnap.exe"

echo Starting PegSnap (Apple Minimalist Edition)...
start "" "%EXE_PATH%"
echo PegSnap is running live in the background!
