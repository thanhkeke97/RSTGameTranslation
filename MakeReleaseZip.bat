SET APP_NAME=RSTGameTranslation

rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean UGTLive.sln --configuration Release
dotnet publish .\UGTLive.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false


mkdir tempbuild
copy app\win-x64\publish\*.exe tempbuild
copy app\chatgpt_config.txt tempbuild
copy app\gemini_config.txt tempbuild
copy app\ollama_config.txt tempbuild

:the server stuff too
mkdir tempbuild\webserver
copy app\webserver\*.py tempbuild\webserver
copy app\webserver\*.bat tempbuild\webserver
pause