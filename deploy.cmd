@echo off
rem Copies the published launcher into an FGO Arcade install.
rem   deploy.cmd D:\games\FGOA
setlocal
set "ROOT=%~dp0"
if "%~1"=="" (
  echo Usage: deploy.cmd ^<install root^>  - the folder that holds App and Server.
  exit /b 2
)
if not exist "%~1\App" (
  echo "%~1" is not an FGO Arcade install: it has no App folder.
  exit /b 2
)
if not exist "%ROOT%dist\FGOAC scooby.exe" (
  echo Nothing to deploy. Run publish.cmd first.
  exit /b 1
)
copy /y "%ROOT%dist\FGOAC scooby.exe" "%~1\FGOAC scooby.exe" || exit /b 1
echo Deployed to "%~1\FGOAC scooby.exe"
endlocal
