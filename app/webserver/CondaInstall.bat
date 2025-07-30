@echo off
echo ========================================
echo Installing conda to current directory
echo ========================================

REM Get current directory
echo Current directory: %CD%
set "INSTALL_PATH=%CD%"
echo.

REM Download Miniconda
echo Downloading Miniconda....
curl -L -o miniconda.exe https://repo.anaconda.com/miniconda/Miniconda3-py39_25.3.1-1-Windows-x86_64.exe

REM Check download success
if not exist miniconda.exe (
    echo Error, can not download Miniconda!
    pause
    exit /b 1
)

REM Install Miniconda
echo Installing Miniconda....
miniconda.exe /InstallationType=JustMe /AddToPath=1 /RegisterPython=0 /S /D=%INSTALL_PATH%\Miniconda3


REM Delete Miniconda installer
del miniconda.exe

echo.
echo ========================================
echo Install success on directory: %INSTALL_PATH%
echo Now you can close this windows!
pause
