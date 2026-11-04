@echo off
setlocal

title jRandomSkills VPK Deploy

set "SOURCE=F:\Pulpit\Projekty\Github clone\jRandomSkills\VPK Addon\build\jRandomSkills.vpk"

set "SERVER=F:\CS2 Server\server\game\csgo\overrides"
set "CLIENT=D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo\overrides"

echo.
echo ============================================================
echo               jRandomSkills VPK DEPLOY
echo ============================================================
echo.

if not exist "%SOURCE%" (
    echo [ERROR] VPK not found.:
    echo %SOURCE%
    echo.
    pause
    exit /b 1
)

echo [INFO] Copying to server...
echo %SERVER%
echo.

if not exist "%SERVER%" (
    echo [INFO] Creating server overrides directory...
    mkdir "%SERVER%" >nul 2>&1
)

copy /Y "%SOURCE%" "%SERVER%\jRandomSkills.vpk" >nul

if errorlevel 1 (
    echo [ERROR] Failed to copy VPK to server.
    echo.
    pause
    exit /b 1
)

echo [OK] Server.

echo.
echo [INFO] Copying to client...
echo %CLIENT%
echo.

if not exist "%CLIENT%" (
    echo [INFO] Creating client overrides directory...
    mkdir "%CLIENT%" >nul 2>&1
)

copy /Y "%SOURCE%" "%CLIENT%\jRandomSkills.vpk" >nul

if errorlevel 1 (
    echo [ERROR] Failed to copy VPK to client.
    echo.
    pause
    exit /b 1
)

echo [OK] Client.

echo.
echo ============================================================
echo                         DEPLOY OK
echo ============================================================
echo.
echo VPK:
echo %SOURCE%
echo.
echo SERVER:
echo %SERVER%\jRandomSkills.vpk
echo.
echo CLIENT:
echo %CLIENT%\jRandomSkills.vpk
echo.
echo ============================================================
echo.

pause