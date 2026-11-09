@echo off
cd /d "%~dp0"
dotnet build -c Debug
if errorlevel 1 exit /b 1
echo.
echo done: ..\plugins\MochaPatches0629.dll
