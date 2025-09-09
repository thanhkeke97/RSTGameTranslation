@echo off
echo ===== Setting up an PaddleOCR with NVidia GPU Support Conda Env =====
echo 
echo This is something you only have to do once, it creates (or recreates) a Conda environment called "ocrstuffpaddleocr".
echo This can take quite a long time to setup.  It's for NVidia cards, you'll have to hack this script up or do it
echo manually for other card types.
echo

REM Activating base environment
call conda activate base
call conda update -y conda
call conda config --add channels conda-forge
call conda config --set channel_priority strict

echo Removing existing ocrstuffpaddleocr environment if it exists...
call conda env remove -n ocrstuffpaddleocr -y

echo Creating and setting up new Conda environment...
call conda create -y --name ocrstuffpaddleocr python=3.9
call conda activate ocrstuffpaddleocr
echo Installing dependencies
call pip install pillow==11.2.1 matplotlib==3.9.4 scipy==1.13.1 tqdm==4.67.1 pyyaml==6.0.2 requests==2.32.3


REM Install PaddleOCR via pip
echo Installing PaddleOCR...
call pip install paddleocr==3.2.0
call pip install paddlepaddle==3.2.0 -i https://www.paddlepaddle.org.cn/packages/stable/cpu/
call  python -m pip install paddlepaddle-gpu==3.2.0 -i https://www.paddlepaddle.org.cn/packages/stable/cu126/



REM Verify installations
echo Verifying installations...
python -c "from paddleocr import PaddleOCR; print('PaddleOCR imported successfully')"
python -c "import cv2; print('OpenCV Version:', cv2.__version__)"

echo ===== Setup Complete =====
echo If the above looks looks like the test worked, you can now close this window and click "StartServer" to start the server.
pause

