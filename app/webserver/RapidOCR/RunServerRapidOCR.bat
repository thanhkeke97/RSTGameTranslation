@echo off
setlocal

if not exist "ocrstuffrapidocr\Scripts\python.exe" (
	echo [ERROR] Missing python executable: ocrstuffrapidocr\Scripts\python.exe
	exit /b 1
)

set KMP_DUPLICATE_LIB_OK=TRUE
"ocrstuffrapidocr\Scripts\python.exe" "server_rapid.py"
exit /b %ERRORLEVEL%