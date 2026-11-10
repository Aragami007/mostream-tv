@echo off
setlocal
set "APP=%~dp0dist\MostreamTV.Host\MostreamTV.Host.exe"

if not exist "%APP%" (
  echo Mostream TV has not been published yet.
  echo Run: dotnet publish .\MostreamTV.Host\MostreamTV.Host.csproj -c Release -o .\dist\MostreamTV.Host
  pause
  exit /b 1
)

start "" "%APP%"
