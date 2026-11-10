@echo off
setlocal
set "APP=%~dp0dist\MoStreamTV.Host\MoStreamTV.Host.exe"

if not exist "%APP%" (
  echo MoStream TV has not been published yet.
  echo Run: dotnet publish .\MoStreamTV.Host\MoStreamTV.Host.csproj -c Release -o .\dist\MoStreamTV.Host
  pause
  exit /b 1
)

start "" "%APP%"
