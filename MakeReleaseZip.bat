SET APP_NAME=UGTLive

rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean UGTLive.sln --configuration Release
dotnet publish .\UGTLive.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false


mkdir tempbuild
copy app\win-x64\publish\*.exe tempbuild
copy app\chatgpt_config.txt tempbuild
copy app\gemini_config.txt tempbuild
copy app\ollama_config.txt tempbuild
copy media\readme.txt tempbuild

:the server stuff too
mkdir tempbuild\webserver
copy app\webserver\*.py tempbuild\webserver
copy app\webserver\*.bat tempbuild\webserver

call %RT_PROJECTS%\Signing\sign.bat tempbuild\ugtlive.exe "Universal Game Translator Live"

set FNAME=UniversalGameTranslatorLive_Windows.zip
del %FNAME%

%RT_PROJECTS%\proton\shared\win\utils\7za.exe a -r -tzip %FNAME% tempbuild

:Rename the root folder
%RT_PROJECTS%\proton\shared\win\utils\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\

pause