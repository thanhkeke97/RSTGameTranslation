@echo off
echo ===== Setting up EasyOCR with NVidia GPU Support (Custom Python Path) =====
echo.
echo This script creates a standard Python Venv called "ocrstuffeasyocr".
echo.

REM =================================================================
REM CONFIGURATION: SET THE PATH TO YOUR PYTHON.EXE BELOW
REM Using relative path: ..\ means "Go up one folder level"
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
if exist "ocrstuffeasyocr" (
    echo Removing existing ocrstuffeasyocr folder...
    rmdir /s /q "ocrstuffeasyocr"
)

REM 3. Create new venv using the specific Python executable
echo Creating new venv environment...
"%SOURCE_PYTHON%" -m venv ocrstuffeasyocr

REM 4. Activate the environment
echo Activating environment...
call ocrstuffeasyocr\Scripts\activate

REM 5. Install dependencies
echo Upgrading pip...
python -m pip install --upgrade pip

REM Install PyTorch with GPU support (Heavy download ~2-3GB)
echo Installing PyTorch with GPU support (CUDA 12.6)...
call pip install torch==2.7.0 torchvision==0.22.0 torchaudio==2.7.0 --index-url https://download.pytorch.org/whl/cu126

REM Install additional dependencies
echo Installing helper libraries...
call pip install pillow==11.2.1 matplotlib==3.9.4 scipy==1.13.1 tqdm==4.67.1 pyyaml==6.0.2 requests==2.32.3

REM 6. Install EasyOCR via pip
echo Installing EasyOCR...
call pip install easyocr==1.7.2

REM 7. Download language models (This runs python to trigger the download)
echo Downloading language models for EasyOCR (Japanese and English)...
echo This might take a moment...
python -c "import easyocr; reader = easyocr.Reader(['ja', 'en'])"

REM 8. Verification
echo.
echo Verifying installations...
python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)"
python -c "import easyocr; print('EasyOCR imported successfully')"

echo.
echo ===== Setup Complete =====
echo You can now close this window.
echo To run your app later, remember to execute: call ocrstuffeasyocr\Scripts\activate
pause
