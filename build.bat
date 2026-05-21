@echo off
cd /d "%~dp0GTFO_SuperFlashlight"
echo [FO Build] Building...
dotnet build -c Release
if errorlevel 1 (
    echo [FO Build] BUILD FAILED - see errors above
    pause
    exit /b 1
)

set SRC=%LOCALAPPDATA%\GTFO_SuperFlashlight\bin\Release\net6.0\FlashlightOverhaul.dll
set DST=%~dp0FlashlightOverhaul.dll

echo [FO Build] Copying %SRC% -> %DST%
copy /y "%SRC%" "%DST%"
if errorlevel 1 (
    echo [FO Build] COPY FAILED
    pause
    exit /b 1
)
echo [FO Build] Done! DLL updated.
echo [FO Build] (Release folder is left untouched — update it manually when cutting a Thunderstore bundle.)
pause
