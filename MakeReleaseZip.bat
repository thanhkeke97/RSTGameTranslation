SET APP_NAME=RSTGameTranslation
SET APP_VERSION=0.2
SET FNAME=%APP_NAME%_v%APP_VERSION%.zip

del %APP_NAME%_v*.zip
rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean RST.sln --configuration Release
dotnet publish .\RST.csproj -c Release -r win-x64 -p:PublishSingleFile=false --self-contained true


mkdir tempbuild
copy app\win-x64\publish\* tempbuild
copy README.md tempbuild

:the server stuff too
mkdir tempbuild\webserver
mkdir tempbuild\webserver\EasyOCR
mkdir tempbuild\webserver\PaddleOCR
copy app\webserver\EasyOCR\*.py tempbuild\webserver\EasyOCR
copy app\webserver\EasyOCR\*.bat tempbuild\webserver\EasyOCR
copy app\webserver\PaddleOCR\*.bat tempbuild\webserver\PaddleOCR
copy app\webserver\PaddleOCR\*.py tempbuild\webserver\PaddleOCR
copy app\webserver\*.bat tempbuild\webserver

7-zip\7z.exe a -r -tzip %FNAME% tempbuild
:Rename the root folder
7-zip\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
pause