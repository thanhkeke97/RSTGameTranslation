SET APP_NAME=RSTGameTranslation
SET APP_VERSION=5.2
SET FNAME=%APP_NAME%_v%APP_VERSION%.zip
node update-version.js

del %APP_NAME%_v*.zip
rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean RST.sln --configuration Release
:NOTE: PublishSingleFile=true was removed. With single-file mode, managed
:NUGet wrappers like Microsoft.ML.OnnxRuntime.dll get bundled into rst.exe
:(190 MB) and some native loader paths become unreliable, causing crashes
:on first launch with `tts_service=Supertonic`. Self-contained multi-file
:publish (the default) keeps every DLL next to rst.exe, matching the layout
:that we already know works (`app\rst.exe` from a regular `dotnet build`).
dotnet publish .\RST.csproj -c Release -r win-x64 --self-contained true


mkdir tempbuild
robocopy app\win-x64\publish tempbuild /E /NFL /NDL
copy README.md tempbuild

:the webserver stuff too
mkdir tempbuild\webserver
mkdir tempbuild\AudioModel
mkdir tempbuild\Supertonic
mkdir tempbuild\webserver\EasyOCR
mkdir tempbuild\webserver\PaddleOCR
mkdir tempbuild\webserver\RapidOCR
mkdir tempbuild\webserver\Python311
mkdir tempbuild\Languages
copy app\webserver\EasyOCR\*.py tempbuild\webserver\EasyOCR
copy app\webserver\EasyOCR\*.bat tempbuild\webserver\EasyOCR
copy app\webserver\PaddleOCR\*.bat tempbuild\webserver\PaddleOCR
copy app\webserver\PaddleOCR\*.py tempbuild\webserver\PaddleOCR
copy app\webserver\RapidOCR\*.bat tempbuild\webserver\RapidOCR
copy app\webserver\RapidOCR\*.py tempbuild\webserver\RapidOCR
copy app\OneOcr\* tempbuild
:NOTE: OneOcr ships its own onnxruntime.dll (older build) for its own OCR
:engine. If we let it overwrite the NuGet-published Microsoft.ML.OnnxRuntime
:1.20.1 binaries that the Supertonic TTS backend depends on, the
:managed wrapper and the native runtime version-mismatch and the app
:crashes silently on startup. Restore the published native runtime so
:both OCR and TTS can find the version they were built against.
copy /Y app\win-x64\publish\onnxruntime.dll tempbuild\onnxruntime.dll
copy /Y app\win-x64\publish\onnxruntime_providers_shared.dll tempbuild\onnxruntime_providers_shared.dll
del /Q tempbuild\onnxruntime.lib 2>nul
del /Q tempbuild\onnxruntime_providers_shared.lib 2>nul
copy app\AudioModel\ggml-tiny.bin tempbuild\AudioModel
copy app\webserver\*.bat tempbuild\webserver
copy app\Languages\* tempbuild\Languages
robocopy app\webserver\Python311 tempbuild\webserver\Python311 /E /NFL /NDL

7-zip\7z.exe a -r -tzip %FNAME% tempbuild
:Rename the root folder
7-zip\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
pause