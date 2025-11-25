@echo off
echo ===== Setting up PaddleOCR with NVidia GPU Support (Custom Python Path) =====
echo.
echo This script creates a standard Python Venv called "ocrstuffpaddleocr".
echo NOTE: You MUST point to a Python 3.11 executable for this specific GPU wheel to work.
echo.

REM =================================================================
REM CONFIGURATION: SET THE PATH TO YOUR PYTHON.EXE BELOW
REM =================================================================
set "SOURCE_PYTHON=..\Python311\python.exe"


REM 1. Verify if the Python path is correct
if not exist "%SOURCE_PYTHON%" (
    echo [ERROR] Python executable not found at: "%SOURCE_PYTHON%"
    echo Please check the path in the script and try again.
    pause
    exit /b
)

echo Using Python source: "%SOURCE_PYTHON%"

REM 2. Remove existing environment if it exists
if exist "ocrstuffpaddleocr" (
    echo Removing existing ocrstuffpaddleocr folder...
    rmdir /s /q "ocrstuffpaddleocr"
)

REM 3. Create new venv using the specific Python executable
echo Creating new venv environment...
"%SOURCE_PYTHON%" -m venv ocrstuffpaddleocr

REM 4. Activate the environment
echo Activating environment...
call ocrstuffpaddleocr\Scripts\activate

REM 5. Install dependencies
echo Upgrading pip...
python -m pip install --upgrade pip

echo Installing core dependencies...
call pip install pillow==11.2.1 matplotlib==3.9.4 scipy==1.13.1 tqdm==4.67.1 pyyaml==6.0.2 requests==2.32.3

echo Installing PaddleOCR...
call pip install paddleocr==3.2.0

echo Installing PaddlePaddle (CPU Base)...
call pip install paddlepaddle==3.1.1 -i https://www.paddlepaddle.org.cn/packages/stable/cpu/

echo Installing PaddlePaddle (GPU Specific Wheel for Python 3.9)...
REM This wheel is hardcoded for cp39 (Python 3.9)
call pip install https://paddle-qa.bj.bcebos.com/paddle-pipeline/Develop-TagBuild-Training-Windows-Gpu-Cuda12.9-Cudnn9.9-Trt10.5-Mkl-Avx-VS2019-SelfBuiltPypiUse/86d658f56ebf3a5a7b2b33ace48f22d10680d311/paddlepaddle_gpu-3.0.0.dev20250717-cp311-cp311-win_amd64.whl

REM 6. Verification
echo Verifying installations...
python -c "from paddleocr import PaddleOCR; print('PaddleOCR imported successfully')"
python -c "import cv2; print('OpenCV Version:', cv2.__version__)"

echo.
echo ===== Setup Complete =====
echo You can now close this window.
echo To run your app later, remember to execute: call ocrstuffpaddleocr\Scripts\activate
pause
