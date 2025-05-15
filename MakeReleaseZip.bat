SET APP_NAME=RSTGameTranslation
SET APP_VERSION=0.1
SET FNAME=%APP_NAME%_v%APP_VERSION%.zip

rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean UGTLive.sln --configuration Release
dotnet publish .\UGTLive.csproj -c Release -r win-x64 -p:PublishSingleFile=false --self-contained true


mkdir tempbuild
copy app\win-x64\publish\* tempbuild
copy app\chatgpt_config.txt tempbuild
copy app\gemini_config.txt tempbuild
copy app\ollama_config.txt tempbuild
copy README.md tempbuild

:the server stuff too
mkdir tempbuild\webserver
copy app\webserver\*.py tempbuild\webserver
copy app\webserver\*.bat tempbuild\webserver

7-zip\7z.exe a -r -tzip %FNAME% tempbuild
:Rename the root folder
7-zip\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
pause