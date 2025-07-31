SET APP_NAME=RSTGameTranslation
SET APP_VERSION=1.5
SET FNAME=%APP_NAME%_v%APP_VERSION%.zip
node update-version.js

del %APP_NAME%_v*.zip
rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean RST.sln --configuration Release
dotnet publish .\RST.csproj -c Release -r win-x64 -p:PublishSingleFile=false --self-contained true


mkdir tempbuild
mkdir tempbuild\x64
mkdir tempbuild\x86
copy app\win-x64\publish\* tempbuild
copy README.md tempbuild
copy app\x64\* tempbuild\x64
copy app\x86\* tempbuild\x86

:the server stuff too
mkdir tempbuild\webserver
mkdir tempbuild\tessdata
mkdir tempbuild\webserver\EasyOCR
mkdir tempbuild\webserver\PaddleOCR
copy app\webserver\EasyOCR\*.py tempbuild\webserver\EasyOCR
copy app\webserver\EasyOCR\*.bat tempbuild\webserver\EasyOCR
copy app\webserver\PaddleOCR\*.bat tempbuild\webserver\PaddleOCR
copy app\webserver\PaddleOCR\*.py tempbuild\webserver\PaddleOCR
copy app\webserver\*.bat tempbuild\webserver
copy app\tessdata\*.traineddata tempbuild\tessdata

7-zip\7z.exe a -r -tzip %FNAME% tempbuild
:Rename the root folder
7-zip\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
pause