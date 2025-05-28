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
call pip install torch==2.7.0 torchvision==0.22.0 torchaudio==2.7.0 --index-url https://download.pytorch.org/whl/cu118

REM Install additional dependencies
call pip install pillow==11.2.1 matplotlib==3.9.4 scipy==1.13.1 tqdm==4.67.1 pyyaml==6.0.2 requests==2.32.3


REM Install EasyOCR via pip
@REM echo Installing EasyOCR...
call pip install easyocr==1.7.2



REM Download language models for EasyOCR (Japanese and English)
echo Installing language models for EasyOCR...
python -c "import easyocr; reader = easyocr.Reader(['ja', 'en'])"

REM Verify installations
echo Verifying installations...
python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)"
python -c "import easyocr; print('EasyOCR imported successfully')"

echo ===== Setup Complete =====
echo If the above looks looks like the test worked, you can now double click "server_easy.py" for easy ocr and it will load this conda env and run the python server.
