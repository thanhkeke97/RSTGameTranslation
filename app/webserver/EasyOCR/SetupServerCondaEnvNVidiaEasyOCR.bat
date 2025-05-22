@echo off
echo ===== Setting up an EasyOCR with NVidia GPU Support Conda Env =====
echo 
echo This is something you only have to do once, it creates (or recreates) a Conda environment called "ocrstuffeasyocr".
echo This can take quite a long time to setup.  It's for NVidia cards, you'll have to hack this script up or do it
echo manually for other card types.
echo

REM Activating base environment
call conda activate base
call conda update -y conda
call conda config --add channels conda-forge
call conda config --set channel_priority strict

echo Removing existing ocrstuffeasyocr environment if it exists...
call conda env remove -n ocrstuffeasyocr -y

echo Creating and setting up new Conda environment...
call conda create -y --name ocrstuffeasyocr python=3.9
call conda activate ocrstuffeasyocr

REM Install PyTorch with GPU support (includes correct CUDA and cuDNN versions)
echo Installing PyTorch with GPU support...
call conda install -y pytorch torchvision torchaudio pytorch-cuda=11.8 -c pytorch -c nvidia

REM Install additional dependencies
call conda install -y -c conda-forge opencv pillow matplotlib scipy
call conda install -y tqdm pyyaml requests

REM Install EasyOCR via pip
echo Installing EasyOCR...
pip install easyocr --user

@REM REM Install PaddleOCR via pip
@REM echo Installing PaddleOCR...
@REM pip install paddleocr==2.10.0
@REM python -m pip install paddlepaddle==3.0.0rc1 -i https://www.paddlepaddle.org.cn/packages/stable/cpu/
@REM python -m pip install paddlepaddle-gpu==3.0.0rc1 -i https://www.paddlepaddle.org.cn/packages/stable/cu118/


REM Download language models for EasyOCR (Japanese and English)
echo Installing language models for EasyOCR...
python -c "import easyocr; reader = easyocr.Reader(['ja', 'en'])"

REM Verify installations
echo Verifying installations...
python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)"
python -c "import easyocr; print('EasyOCR imported successfully')"
@REM python -c "from paddleocr import PaddleOCR; print('PaddleOCR imported successfully')"
@REM python -c "import cv2; print('OpenCV Version:', cv2.__version__)"

echo ===== Setup Complete =====
echo If the above looks looks like the test worked, you can now double click "server_paddle.py" for paddle ocr or "server_easy.py" for easy ocr and it will load this conda env and run the python server.
pause
