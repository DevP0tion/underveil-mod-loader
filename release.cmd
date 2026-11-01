@echo off
setlocal
title Underveil Mod Loader release
cd /d "%~dp0"

set "BEPINEX_VERSION=5.4.23.6"
set "BEPINEX_SHA256=6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755"
set "BEPINEX_ZIP=BepInEx_win_x64_%BEPINEX_VERSION%.zip"
set "BEPINEX_URL=https://github.com/BepInEx/BepInEx/releases/download/v%BEPINEX_VERSION%/%BEPINEX_ZIP%"
set "BEPINEX_LICENSE_URL=https://raw.githubusercontent.com/BepInEx/BepInEx/v%BEPINEX_VERSION%/LICENSE"

set "VERSION="
for /f tokens^=6^ delims^=^" %%v in ('findstr /c:"[BepInPlugin(" Plugin.cs') do set "VERSION=%%v"
if not defined VERSION (
    echo Could not read the version from Plugin.cs.
    goto fail
)
set "TAG=v%VERSION%"
set "DIST=dist"
set "STAGE=%DIST%\stage"
set "FILES=%STAGE%\UnderveilModLoader_files"
set "ZIP=%DIST%\UnderveilModLoader_%TAG%.zip"

echo Checking the repository for %TAG%...
call gh auth status >nul 2>&1 || (
    echo GitHub CLI is not logged in. Run: gh auth login
    goto fail
)
set "BRANCH="
for /f %%b in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%b"
if not "%BRANCH%"=="main" (
    echo Releases are made from main. The current branch is %BRANCH%.
    goto fail
)
set "DIRTY="
for /f "delims=" %%s in ('git status --porcelain') do set "DIRTY=1"
if defined DIRTY (
    echo There are uncommitted changes. Commit them first, so the release matches the source.
    goto fail
)
git ls-remote --exit-code --tags origin "refs/tags/%TAG%" >nul
if not errorlevel 1 (
    echo %TAG% is already released. Raise the version in Plugin.cs and commit it.
    goto fail
)
if errorlevel 3 (
    echo Could not reach the GitHub repository.
    goto fail
)

if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%FILES%\BepInEx_%BEPINEX_VERSION%" || goto fail

echo Building the loader. The game must be closed...
dotnet build UnderveilModLoader.csproj -c Release -v q --nologo || (
    echo The build failed. Close the game if it is running.
    goto fail
)

echo Downloading BepInEx %BEPINEX_VERSION%...
curl.exe -sSLf -o "%DIST%\%BEPINEX_ZIP%" "%BEPINEX_URL%" || goto fail
curl.exe -sSLf -o "%FILES%\BepInEx-LICENSE.txt" "%BEPINEX_LICENSE_URL%" || goto fail
certutil -hashfile "%DIST%\%BEPINEX_ZIP%" SHA256 | find /i "%BEPINEX_SHA256%" >nul || (
    echo The BepInEx download does not match the expected SHA-256.
    goto fail
)

echo Creating the package...
tar.exe -xf "%DIST%\%BEPINEX_ZIP%" -C "%FILES%\BepInEx_%BEPINEX_VERSION%" || goto fail
copy /y "install.cmd" "%STAGE%\" >nul || goto fail
copy /y "bin\Release\netstandard2.1\UnderveilModLoader.dll" "%FILES%\" >nul || goto fail
copy /y "THIRD-PARTY-NOTICES.txt" "%FILES%\" >nul || goto fail
copy /y "LICENSE" "%FILES%\UnderveilModLoader-LICENSE.txt" >nul || goto fail
tar.exe -a -c -f "%ZIP%" -C "%STAGE%" install.cmd UnderveilModLoader_files || goto fail

echo Publishing %TAG%...
git push origin main || goto fail
call gh release create "%TAG%" "%ZIP%" --target main --title "Underveil Mod Loader %TAG%" --notes "Extract the zip into the game folder, close the game and run install.cmd." || goto fail

echo.
echo Released %TAG%.
pause
exit /b 0

:fail
echo.
echo Release failed. Nothing was published unless a message above says otherwise.
pause
exit /b 1
