@echo off
setlocal EnableExtensions EnableDelayedExpansion

title jRandomSkills Compiler

REM ============================================================
REM CONFIG
REM ============================================================

set "CS2_ROOT=D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"

set "RC=%CS2_ROOT%\game\bin\win64\resourcecompiler.exe"

set "VPKEDIT=F:\Pulpit\Projekty\Github clone\jRandomSkills\VPK Addon\tools\vpkeditcli.exe"

set "PROJECT_DIR=F:\Pulpit\Projekty\Github clone\jRandomSkills\VPK Addon"

set "CONTENT_DIR=%PROJECT_DIR%\Content"

set "CS2_CONTENT=%CS2_ROOT%\content\csgo_addons\jRandomSkills"

set "CS2_GAME=%CS2_ROOT%\game\csgo_addons\jRandomSkills"

set "BUILD_DIR=%PROJECT_DIR%\build"

set "PACKAGE_DIR=%BUILD_DIR%\package"

set "OUTPUT_VPK=%BUILD_DIR%\jRandomSkills.vpk"


REM ============================================================
REM COUNTERS
REM ============================================================

set /a COMPILE_TOTAL=0
set /a COMPILE_OK=0
set /a COMPILE_FAILED=0


REM ============================================================
REM HEADER
REM ============================================================

cls

echo ============================================================
echo              jRandomSkills COMPILER
echo ============================================================
echo.
echo CS2 ROOT:
echo %CS2_ROOT%
echo.
echo CONTENT:
echo %CONTENT_DIR%
echo.
echo RESOURCE COMPILER:
echo %RC%
echo.
echo VPKEDIT:
echo %VPKEDIT%
echo.
echo ============================================================
echo.


REM ============================================================
REM CHECK REQUIRED FILES
REM ============================================================

if not exist "%RC%" (
    echo [ERROR] resourcecompiler.exe not found:
    echo %RC%
    echo.
    pause
    exit /b 1
)

if not exist "%VPKEDIT%" (
    echo [ERROR] vpkeditcli.exe not found:
    echo %VPKEDIT%
    echo.
    pause
    exit /b 1
)

if not exist "%CONTENT_DIR%" (
    echo [ERROR] Content directory not found:
    echo %CONTENT_DIR%
    echo.
    pause
    exit /b 1
)


REM ============================================================
REM CLEAN OLD BUILD
REM ============================================================

echo [1/9] Cleaning old build...
echo.

if exist "%BUILD_DIR%" (
    rmdir /s /q "%BUILD_DIR%"
)

mkdir "%BUILD_DIR%" >nul 2>&1
mkdir "%PACKAGE_DIR%" >nul 2>&1

echo [OK] Build directory ready.
echo.


REM ============================================================
REM CLEAN TEMPORARY CS2 CONTENT
REM ============================================================

echo [2/9] Cleaning temporary CS2 addon directories...
echo.

if exist "%CS2_CONTENT%" (
    rmdir /s /q "%CS2_CONTENT%"
)

if exist "%CS2_GAME%" (
    rmdir /s /q "%CS2_GAME%"
)

mkdir "%CS2_CONTENT%" >nul 2>&1
mkdir "%CS2_GAME%" >nul 2>&1

echo [OK] Temporary directories ready.
echo.


REM ============================================================
REM COPY SOURCE CONTENT TO CS2
REM ============================================================

echo [3/9] Copying source content to CS2...
echo.

robocopy "%CONTENT_DIR%" "%CS2_CONTENT%" /E /NFL /NDL /NJH /NJS /NP ^
    /XD ".git" ".github" "build" "node_modules"

if errorlevel 8 (
    echo.
    echo [ERROR] robocopy failed.
    pause
    exit /b 1
)

echo.
echo [OK] Content copied.
echo.


REM ============================================================
REM CREATE VTEX FILES FROM PNG
REM ============================================================


echo [4/9] Creating VTEX files from PNG...
echo.

set "CS2_CONTENT=%CS2_CONTENT%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%\GenerateVtex.ps1"

if errorlevel 1 (
    echo [ERROR] VTEX generation failed.
    pause
    exit /b 1
)

echo.
echo [OK] VTEX generation finished.
echo.



REM ============================================================
REM COMPILE PANORAMA XML
REM ============================================================

echo ============================================================
echo [5/9] COMPILING PANORAMA XML
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.xml) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE PANORAMA VXML
REM ============================================================

echo.
echo ============================================================
echo COMPILING PANORAMA VXML
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.vxml) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE PANORAMA CSS
REM ============================================================

echo.
echo ============================================================
echo COMPILING PANORAMA CSS
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.css) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE PANORAMA VCSS
REM ============================================================

echo.
echo ============================================================
echo COMPILING PANORAMA VCSS
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.vcss) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE SVG
REM
REM IMPORTANT:
REM SVG -> VSVG -> VSVG_C
REM NOT SVG_C
REM ============================================================

echo.
echo ============================================================
echo COMPILING SVG
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.svg) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE VSVG
REM ============================================================

echo.
echo ============================================================
echo COMPILING VSVG
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.vsvg) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE VTEX
REM ============================================================

echo.
echo ============================================================
echo COMPILING VTEX
echo ============================================================
echo.

for /r "%CS2_CONTENT%\panorama" %%F in (*.vtex) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE POSTPROCESSING
REM ============================================================

echo.
echo ============================================================
echo COMPILING POSTPROCESSING
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vpost) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE MODELS
REM ============================================================

echo.
echo ============================================================
echo COMPILING MODELS
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vmdl) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE MATERIALS
REM ============================================================

echo.
echo ============================================================
echo COMPILING MATERIALS
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vmat) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE TEXTURES
REM
REM Only compile existing .vtex files.
REM PNG/JPG are NOT directly compiled here.
REM ============================================================

echo.
echo ============================================================
echo COMPILING TEXTURES
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vtex) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE PARTICLES
REM ============================================================

echo.
echo ============================================================
echo COMPILING PARTICLES
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vpcf) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE VDATA
REM ============================================================

echo.
echo ============================================================
echo COMPILING VDATA
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vdata) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE VSEQ
REM ============================================================

echo.
echo ============================================================
echo COMPILING VSEQ
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vseq) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE SOUND EVENT FILES
REM ============================================================

echo.
echo ============================================================
echo COMPILING VSNDEVTS
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vsndevts) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE SOUND STACKS
REM ============================================================

echo.
echo ============================================================
echo COMPILING V SND STACKS
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vsndstck) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILE VSND
REM ============================================================

echo.
echo ============================================================
echo COMPILING VSND
echo ============================================================
echo.

for /r "%CS2_CONTENT%" %%F in (*.vsnd) do (
    call :COMPILE_FILE "%%~fF"
)


REM ============================================================
REM COMPILATION SUMMARY
REM ============================================================

echo.
echo ============================================================
echo COMPILATION SUMMARY
echo ============================================================
echo.

echo Attempts : %COMPILE_TOTAL%
echo Success  : %COMPILE_OK%
echo Failed   : %COMPILE_FAILED%

echo.


REM ============================================================
REM COPY GAME OUTPUTS TO PACKAGE
REM ============================================================

echo ============================================================
echo [6/9] Collecting compiled files...
echo ============================================================
echo.

if exist "%CS2_GAME%" (
    robocopy "%CS2_GAME%" "%PACKAGE_DIR%" /E /NFL /NDL /NJH /NJS /NP
)

echo.
echo [OK] Game output copied.
echo.


REM ============================================================
REM COPY ORIGINAL CONTENT
REM
REM This keeps PNG/JPG/GIF/etc.
REM but does NOT copy source resource files that should be compiled.
REM ============================================================

echo ============================================================
echo [7/9] Copying non-compiled assets...
echo ============================================================
echo.

robocopy "%CONTENT_DIR%" "%PACKAGE_DIR%" /E /NFL /NDL /NJH /NJS /NP ^
    /XF *.xml *.vxml *.css *.vcss *.txt *.vtex *.svg *.vsvg *.vpost ^
        *.vmdl *.vmat *.vtex *.vpcf *.vdata *.vseq ^
        *.vsndevts *.vsndstck *.vsnd *.png *.jpg *.jpeg ^
    /XD ".git" ".github" "build" "node_modules"

if errorlevel 8 (
    echo.
    echo [ERROR] Failed while copying non-compiled assets.
    pause
    exit /b 1
)

echo.
echo [OK] Non-compiled assets copied.
echo.


REM ============================================================
REM REMOVE SOURCE RESOURCE FILES FROM PACKAGE
REM ============================================================

echo ============================================================
echo Removing source resource files from package...
echo ============================================================
echo.

for /r "%PACKAGE_DIR%" %%F in (*.xml) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vxml) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.css) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vcss) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vtex) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.svg) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vsvg) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vpost) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vmdl) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vmat) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vtex) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vpcf) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vdata) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vseq) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vsndevts) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vsndstck) do (
    del /f /q "%%~fF" >nul 2>&1
)

for /r "%PACKAGE_DIR%" %%F in (*.vsnd) do (
    del /f /q "%%~fF" >nul 2>&1
)

echo [OK] Source files removed.
echo.


REM ============================================================
REM SHOW COMPILED FILES
REM ============================================================

echo ============================================================
echo [8/9] Checking compiled resources...
echo ============================================================
echo.

echo --- VXML_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vxml_c) do (
    echo %%~fF
)

echo.
echo --- VCSS_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vcss_c) do (
    echo %%~fF
)

echo.
echo --- VSVG_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vsvg_c) do (
    echo %%~fF
)

echo.
echo --- VTEX_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vtex_c) do (
    echo %%~fF
)

echo.
echo --- VPOST_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vpost_c) do (
    echo %%~fF
)

echo.
echo --- VMDL_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vmdl_c) do (
    echo %%~fF
)

echo.
echo --- VMAT_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vmat_c) do (
    echo %%~fF
)

echo.
echo --- VTEX_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vtex_c) do (
    echo %%~fF
)

echo.
echo --- VPCF_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vpcf_c) do (
    echo %%~fF
)

echo.
echo --- VDATA_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vdata_c) do (
    echo %%~fF
)

echo.
echo --- VSND_C ---
for /r "%PACKAGE_DIR%" %%F in (*.vsnd_c) do (
    echo %%~fF
)

echo.


REM ============================================================
REM CREATE VPK
REM ============================================================

echo ============================================================
echo [9/9] Creating VPK...
echo ============================================================
echo.

if exist "%OUTPUT_VPK%" (
    del /f /q "%OUTPUT_VPK%"
)

"%VPKEDIT%" "%PACKAGE_DIR%" -t vpk -s -o "%OUTPUT_VPK%"

if errorlevel 1 (
    echo.
    echo ============================================================
    echo [ERROR] VPK creation failed.
    echo ============================================================
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo BUILD COMPLETE
echo ============================================================
echo.
echo VPK:
echo %OUTPUT_VPK%
echo.

if exist "%OUTPUT_VPK%" (
    for %%A in ("%OUTPUT_VPK%") do (
        echo Size: %%~zA bytes
    )
)

echo.
echo Compile attempts : %COMPILE_TOTAL%
echo Compile success  : %COMPILE_OK%
echo Compile failed   : %COMPILE_FAILED%
echo.

if %COMPILE_FAILED% GTR 0 (
    echo [WARNING] Some resources failed to compile.
    echo The VPK was still created.
    echo.
)

echo ============================================================
echo.


REM ============================================================
REM CLEAN TEMPORARY CS2 DIRECTORIES
REM ============================================================

echo Cleaning temporary CS2 directories...
echo.

if exist "%CS2_CONTENT%" (
    rmdir /s /q "%CS2_CONTENT%"
)

if exist "%CS2_GAME%" (
    rmdir /s /q "%CS2_GAME%"
)

echo [OK] Temporary directories removed.
echo.


REM ============================================================
REM DONE
REM ============================================================

echo ============================================================
echo              jRandomSkills BUILD FINISHED
echo ============================================================
echo.
echo Output:
echo %OUTPUT_VPK%
echo.
pause
exit /b 0


REM ============================================================
REM COMPILE SINGLE FILE
REM ============================================================

:COMPILE_FILE

set "INPUT_FILE=%~1"

if not exist "%INPUT_FILE%" (
    exit /b 0
)

set /a COMPILE_TOTAL+=1

echo.
echo ------------------------------------------------------------
echo COMPILING
echo ------------------------------------------------------------
echo %INPUT_FILE%
echo ------------------------------------------------------------

"%RC%" -i "%INPUT_FILE%" -f -nop4 -v -pauseiferror

if errorlevel 1 (
    echo.
    echo [FAILED] %INPUT_FILE%
    set /a COMPILE_FAILED+=1
    exit /b 0
)

echo.
echo [OK] %INPUT_FILE%

set /a COMPILE_OK+=1

exit /b 0