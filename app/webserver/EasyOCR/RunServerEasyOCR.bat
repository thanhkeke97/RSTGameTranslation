@echo off
setlocal

if not exist "ocrstuffeasyocr\Scripts\python.exe" (
	echo [ERROR] Missing python executable: ocrstuffeasyocr\Scripts\python.exe
	exit /b 1
)

set KMP_DUPLICATE_LIB_OK=TRUE
"ocrstuffeasyocr\Scripts\python.exe" "server_easy.py"
exit /b %ERRORLEVEL%