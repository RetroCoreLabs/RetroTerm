@echo off
setlocal

:: RetroTerm Build & Publish Script
:: Builds both client and test server to publish/ folder

echo ========================================================================
echo   RetroTerm Release Build
echo ========================================================================
echo.

set "PUBLISH_DIR=.\publish"
set "CLIENT_PROJ=.\src\RetroTerm.Desktop\RetroTerm.Desktop.csproj"
set "SERVER_PROJ=.\tests\RetroTerm.TestServer\RetroTerm.TestServer.csproj"

:: Create publish directory if it doesn't exist
if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"

:: Kill any running instances silently
taskkill /F /IM RetroTerm.TestServer.exe >nul 2>&1
taskkill /F /IM RetroTerm.Desktop.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo.

:: Build RetroTerm.Desktop
echo [1/2] Building RetroTerm.Desktop.exe...
echo ========================================================================
dotnet publish "%CLIENT_PROJ%" -r win-x64 -c Release --self-contained true -o "%PUBLISH_DIR%"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: RetroTerm.Desktop build failed!
    pause
    exit /b 1
)

:: Proactively copy native DLLs into publish root (SDK may place them under runtimes/*)
for %%F in (libSkiaSharp.dll libHarfBuzzSharp.dll av_libglesv2.dll) do (
    if not exist "%PUBLISH_DIR%\%%F" (
        for /r "%PUBLISH_DIR%" %%S in (%%F) do (
            copy /Y "%%S" "%PUBLISH_DIR%" >nul
        )
        if not exist "%PUBLISH_DIR%\%%F" (
            for /r ".\src\RetroTerm.Desktop\bin\Release" %%S in (%%F) do (
                copy /Y "%%S" "%PUBLISH_DIR%" >nul
            )
        )
    )
)

:: Verify native DLLs are present
echo Verifying native libraries...
if not exist "%PUBLISH_DIR%\libSkiaSharp.dll" (
    echo ERROR: libSkiaSharp.dll missing!
    pause
    exit /b 1
)
if not exist "%PUBLISH_DIR%\av_libglesv2.dll" (
    echo ERROR: av_libglesv2.dll missing!
    pause
    exit /b 1
)
if not exist "%PUBLISH_DIR%\libHarfBuzzSharp.dll" (
    echo ERROR: libHarfBuzzSharp.dll missing!
    pause
    exit /b 1
)
echo Native libraries OK

echo.
echo [OK] RetroTerm.Desktop.exe published successfully
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

:: Show results
echo ========================================================================
echo   Build Complete!
echo ========================================================================
echo.
echo Published to: %PUBLISH_DIR%
echo.
echo Files created:
echo   - RetroTerm.Desktop.exe
echo   - RetroTerm.TestServer.exe
echo.
echo To test:
echo   1. Terminal 1: cd publish ^&^& RetroTerm.TestServer.exe 2323
echo   2. Terminal 2: cd publish ^&^& RetroTerm.Desktop.exe
echo   3. Connect to localhost:2323
echo.
echo ========================================================================

endlocal
pause

