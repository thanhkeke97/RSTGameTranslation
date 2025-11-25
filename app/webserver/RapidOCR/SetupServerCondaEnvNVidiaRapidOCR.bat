@echo off
echo ===== Setting up RapidOCR with NVidia/DirectML Support (Custom Python Path) =====
echo.
echo This script creates a standard Python Venv called "ocrstuffrapidocr".
echo.

REM =================================================================
REM CONFIGURATION: SET THE PATH TO YOUR PYTHON.EXE BELOW
REM Note: The original script asked for Python 3.12. 
REM =================================================================
set "SOURCE_PYTHON=..\Python311\python.exe"


REM 1. Verify if the Python path is correct
if not exist "%SOURCE_PYTHON%" (
    echo [ERROR] Python executable not found at: "%SOURCE_PYTHON%"
    echo Please check if the folder exists next to this project folder.
    pause
    exit /b
)

echo Using Python source: "%SOURCE_PYTHON%"

REM 2. Remove existing environment if it exists
if exist "ocrstuffrapidocr" (
    echo Removing existing ocrstuffrapidocr folder...
    rmdir /s /q "ocrstuffrapidocr"
)

REM 3. Create new venv using the specific Python executable
echo Creating new venv environment...
"%SOURCE_PYTHON%" -m venv ocrstuffrapidocr

REM 4. Activate the environment
echo Activating environment...
call ocrstuffrapidocr\Scripts\activate

REM 5. Install dependencies
echo Upgrading pip...
python -m pip install --upgrade pip

echo Installing core dependencies...
call pip install pillow==11.2.1 matplotlib==3.9.4 scipy==1.13.1 tqdm==4.67.1 pyyaml==6.0.2 requests==2.32.3

REM 6. Install RapidOCR specific packages
echo Installing RapidOCR...
REM Using the versions specified in your original script
call pip install rapidocr==3.4.0
call pip install onnxruntime-directml==1.22.0

REM 7. Verification
echo Verifying installations...
python -c "from rapidocr import RapidOCR; print('RapidOCR imported successfully')"

echo.
echo ===== Setup Complete =====
echo You can now close this window.
echo To run your app later, remember to execute: call ocrstuffrapidocr\Scripts\activate
pause
