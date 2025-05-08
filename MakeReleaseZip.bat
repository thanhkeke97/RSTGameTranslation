SET APP_NAME=RSTGameTranslation

rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean UGTLive.sln --configuration Release
dotnet publish .\UGTLive.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true


mkdir tempbuild
copy app\* tempbuild

:the server stuff too
mkdir tempbuild\webserver
copy app\webserver\*.py tempbuild\webserver
copy app\webserver\*.bat tempbuild\webserver
pause