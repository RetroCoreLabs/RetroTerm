@echo off
REM Start RetroTerm with Test Server
REM This batch file starts both the test server and RetroTerm application

echo.
echo ========================================
echo  RetroTerm Launcher with Test Server
echo ========================================
echo.
echo Starting Test Server on port 8080...
echo.

REM Start test server in a new window
start "RetroTerm Test Server" /MIN publish\RetroTerm.TestServer.exe 8080

REM Wait a moment for server to start
timeout /t 2 /nobreak >nul

echo Test Server started (minimized window)
echo.
echo Starting RetroTerm...
echo.
echo To connect to test server:
echo   File -^> Connect
echo   Host: localhost
echo   Port: 8080
echo.

REM Start RetroTerm (this will block until RetroTerm closes)
start publish\RetroTerm.Desktop.exe

