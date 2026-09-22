@echo off
rem Start latest Release build relative to this script (A13: no hardcoded user path)
setlocal
set "EXE=%~dp0bin\Release\net8.0-windows\TodoSidebar.exe"
if exist "%EXE%" (
  start "" "%EXE%"
) else (
  echo Release build not found: %EXE%
  echo Run: dotnet build -c Release
  pause
)
