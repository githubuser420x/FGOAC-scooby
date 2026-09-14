@echo off
rem Publishes the self-contained single-file host and copies it to dist\FGOAC scooby.exe.
setlocal
set "ROOT=%~dp0"
dotnet publish "%ROOT%src\FGOLocalPlatform.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true || exit /b 1
if not exist "%ROOT%dist" mkdir "%ROOT%dist"
copy /y "%ROOT%src\bin\Release\net6.0-windows\win-x64\publish\FGOLocalPlatform.exe" "%ROOT%dist\FGOAC scooby.exe" || exit /b 1
echo Published "%ROOT%dist\FGOAC scooby.exe"
endlocal
