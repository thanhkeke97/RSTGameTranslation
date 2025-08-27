@echo off
echo ===== Setting up an PaddleOCR with NVidia GPU Support Conda Env =====
echo 
echo This is something you only have to do once, it creates (or recreates) a Conda environment called "ocrstuffrapidocr".
echo This can take quite a long time to setup.  It's for NVidia cards, you'll have to hack this script up or do it
echo manually for other card types.
echo

REM Activating base environment
call conda activate base
call conda update -y conda
call conda config --add channels conda-forge
call conda config --set channel_priority strict

echo Removing existing ocrstuffrapidocr environment if it exists...
call conda env remove -n ocrstuffrapidocr -y

echo Creating and setting up new Conda environment...
call conda create -y --name ocrstuffrapidocr python=3.9
call conda activate ocrstuffrapidocr
echo Installing dependencies
call pip install pillow==11.2.1 matplotlib==3.9.4 scipy==1.13.1 tqdm==4.67.1 pyyaml==6.0.2 requests==2.32.3


REM Install RapidOCR via pip
echo Installing RapidOCR...
call pip install rapidocr==3.3.1
call pip install onnxruntime-directml==1.19.2



REM Verify installations
echo Verifying installations...
python -c "from rapidocr import RapidOCR; print('RapidOCR imported successfully')"

echo ===== Setup Complete =====
echo If the above looks looks like the test worked, you can now close this window and click "StartServer" to start the server.
pause

