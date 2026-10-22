SET APP_NAME=RSTGameTranslation
SET APP_VERSION=6.1
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

mkdir tempbuild\AudioModel
mkdir tempbuild\Supertonic
mkdir tempbuild\Languages
copy app\OneOcr\* tempbuild
:NOTE: oneocr.dll runs on the NuGet-published Microsoft.ML.OnnxRuntime 1.20.1
:that the Supertonic TTS backend also needs, so app\OneOcr no longer ships its
:own onnxruntime.dll (an older one used to overwrite it here, and the managed
:wrapper / native runtime mismatch crashed the app silently on startup).
:The copies below keep the published runtime in place regardless.
copy /Y app\win-x64\publish\onnxruntime.dll tempbuild\onnxruntime.dll
copy /Y app\win-x64\publish\onnxruntime_providers_shared.dll tempbuild\onnxruntime_providers_shared.dll
del /Q tempbuild\onnxruntime.lib 2>nul
del /Q tempbuild\onnxruntime_providers_shared.lib 2>nul
copy app\AudioModel\ggml-tiny.bin tempbuild\AudioModel
copy app\Languages\* tempbuild\Languages

7-zip\7z.exe a -r -tzip %FNAME% tempbuild
:Rename the root folder
7-zip\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
pause