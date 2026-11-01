@echo off
setlocal
title Underveil Mod Loader installer
cd /d "%~dp0"
set "FILES=UnderveilModLoader_files"
set "BEPINEX_VERSION=5.4.23.6"

if not exist "Underveil.exe" (
    echo Underveil.exe was not found next to this file.
    echo Extract the whole zip into the game folder, then run install.cmd there.
    goto fail
)
if not exist "%FILES%\UnderveilModLoader.dll" (
    echo The %FILES% folder is missing.
    echo Extract the whole zip into the game folder, then run install.cmd there.
    goto fail
)

tasklist /fi "imagename eq Underveil.exe" 2>nul | find /i "Underveil.exe" >nul
if not errorlevel 1 (
    echo Underveil is running. Close the game and run install.cmd again.
    goto fail
)

if exist "BepInEx\core\BepInEx.dll" if exist "winhttp.dll" (
    echo BepInEx is already installed. Keeping it.
    goto install_loader
)
if exist "winhttp.dll" (
    echo This folder has a winhttp.dll that does not belong to BepInEx.
    echo Another mod tool may be using it, so nothing was changed.
    goto fail
)

echo Installing BepInEx %BEPINEX_VERSION%...
xcopy "%FILES%\BepInEx_%BEPINEX_VERSION%" "." /e /i /y /q >nul
if errorlevel 1 goto copy_failed

:install_loader
echo Installing Underveil Mod Loader...
if not exist "BepInEx\plugins" mkdir "BepInEx\plugins" || goto copy_failed
copy /y "%FILES%\UnderveilModLoader.dll" "BepInEx\plugins\" >nul || goto copy_failed
copy /y "%FILES%\UnderveilModLoader.xml" "BepInEx\plugins\" >nul || goto copy_failed

echo.
echo Done. Start the game and look for the Mods button on the title screen.
echo Put mods in the Mods folder, which is created on the first start.
pause
exit /b 0

:copy_failed
echo Could not copy the files. If the game is in Program Files, run install.cmd as administrator.

:fail
echo.
echo Installation failed.
pause
exit /b 1
