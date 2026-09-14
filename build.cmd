@echo off
rem Builds the managed assembly only (fast syntax/compile check).
setlocal
set "ROOT=%~dp0"
dotnet build "%ROOT%src\FGOLocalPlatform.csproj" -c Release
endlocal
