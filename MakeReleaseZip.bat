SET APP_NAME=RSTGameTranslation
SET APP_VERSION=3.4
SET FNAME=%APP_NAME%_v%APP_VERSION%.zip
node update-version.js

del %APP_NAME%_v*.zip
rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean RST.sln --configuration Release
dotnet publish .\RST.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true


mkdir tempbuild
copy app\win-x64\publish\* tempbuild
copy README.md tempbuild

:the server stuff too
mkdir tempbuild\webserver
mkdir tempbuild\translation_server
mkdir tempbuild\translation_server\templates
mkdir tempbuild\webserver\EasyOCR
mkdir tempbuild\webserver\PaddleOCR
mkdir tempbuild\webserver\RapidOCR
mkdir tempbuild\webserver\Python311
copy app\webserver\EasyOCR\*.py tempbuild\webserver\EasyOCR
copy app\webserver\EasyOCR\*.bat tempbuild\webserver\EasyOCR
copy app\webserver\PaddleOCR\*.bat tempbuild\webserver\PaddleOCR
copy app\webserver\PaddleOCR\*.py tempbuild\webserver\PaddleOCR
copy app\webserver\RapidOCR\*.bat tempbuild\webserver\RapidOCR
copy app\webserver\RapidOCR\*.py tempbuild\webserver\RapidOCR
copy app\translation_server\*.py tempbuild\translation_server
copy app\translation_server\*.bat tempbuild\translation_server
copy app\OneOcr\* tempbuild
copy app\translation_server\templates\*.html tempbuild\translation_server\templates
copy app\webserver\*.bat tempbuild\webserver
robocopy app\webserver\Python311 tempbuild\webserver\Python311 /E /NFL /NDL

7-zip\7z.exe a -r -tzip %FNAME% tempbuild
:Rename the root folder
7-zip\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
pause