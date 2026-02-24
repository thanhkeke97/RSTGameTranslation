@echo off
setlocal

if not exist "ocrstuffpaddleocr\Scripts\python.exe" (
	echo [ERROR] Missing python executable: ocrstuffpaddleocr\Scripts\python.exe
	exit /b 1
)

set KMP_DUPLICATE_LIB_OK=TRUE
"ocrstuffpaddleocr\Scripts\python.exe" "server_paddle.py"
exit /b %ERRORLEVEL%