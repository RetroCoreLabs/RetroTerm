@echo off
setlocal
cd /d "%~dp0.."

:: RetroTerm Build & Publish Script
:: Builds both client and test server to publish/ folder

echo ========================================================================
echo   RetroTerm Release Build
echo ========================================================================
echo.

set "PUBLISH_DIR=.\publish"
set "CLIENT_PROJ=.\RetroTerm.Desktop\RetroTerm.Desktop.csproj"
set "SERVER_PROJ=.\tests\RetroTerm.TestServer\RetroTerm.TestServer.csproj"

:: Create publish directory if it doesn't exist
if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"

:: Kill any running instances silently
taskkill /F /IM RetroTerm.TestServer.exe >nul 2>&1
taskkill /F /IM RetroTerm.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo.

:: Build RetroTerm.Desktop
echo [1/2] Building RetroTerm.exe...
echo ========================================================================
dotnet publish "%CLIENT_PROJ%" -r win-x64 -c Release --self-contained true -o "%PUBLISH_DIR%"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: RetroTerm.Desktop build failed!
    pause
    exit /b 1
)

:: The exe is a self-contained single file (PublishSingleFile + IncludeNativeLibrariesForSelfExtract
:: in RetroTerm.Desktop.csproj), so libSkiaSharp, libHarfBuzzSharp and av_libglesv2 are inside it
:: and unpacked at first start. Nothing is copied beside it. Until 29 September 2026 this script
:: copied those three DLLs in from bin\Release and refused to finish without them, which left
:: three files from 2024 and 2025 next to every new exe that never loaded them.

echo.
echo [OK] RetroTerm.exe published successfully
echo.

:: Build RetroTerm.TestServer
echo [2/2] Building RetroTerm.TestServer.exe...
echo ========================================================================
dotnet publish "%SERVER_PROJ%" -r win-x64 -c Release -p:PublishSingleFile=true --self-contained true -o "%PUBLISH_DIR%"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: RetroTerm.TestServer build failed!
    pause
    exit /b 1
)
echo.
echo [OK] RetroTerm.TestServer.exe published successfully
echo.

:: The three distribution files docs\DISTRIBUTION-README.md describes go beside the exe
:: (BUGS.md B19, fixed 24 November 2026 - until then the folder held only the programs).
:: The release workflow (.github\workflows\build.yml, step "Distribution files") copies the same
:: three into every download.
copy /Y "..\docs\DISTRIBUTION-README.md" "%PUBLISH_DIR%\README.md" >nul || goto :copyfailed
copy /Y "..\docs\distribution-mcp.json" "%PUBLISH_DIR%\.mcp.json" >nul || goto :copyfailed
copy /Y ".\tools\add-mcp-to-claude-code.ps1" "%PUBLISH_DIR%\add-mcp-to-claude-code.ps1" >nul || goto :copyfailed
echo [OK] README.md, .mcp.json and add-mcp-to-claude-code.ps1 copied
echo.

:: Show results
echo ========================================================================
echo   Build Complete!
echo ========================================================================
echo.
echo Published to: %PUBLISH_DIR%
echo.
echo Files created:
echo   - RetroTerm.exe
echo   - RetroTerm.TestServer.exe
echo   - README.md, .mcp.json, add-mcp-to-claude-code.ps1
echo.
echo To test:
echo   1. Terminal 1: cd publish ^&^& RetroTerm.TestServer.exe 2323
echo   2. Terminal 2: cd publish ^&^& RetroTerm.exe
echo   3. Connect to localhost:2323
echo.
echo ========================================================================

endlocal
pause
exit /b 0

:copyfailed
echo.
echo ERROR: copying the distribution files failed!
pause
exit /b 1

