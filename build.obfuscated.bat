@echo off
cd /d "%~dp0"
dotnet build -c Release -t:Obfuscate
if errorlevel 1 (
    echo.
    echo build failed
    exit /b 1
)
echo.
echo done: dist\MochaPatches0629.dll ^& ..\plugins\MochaPatches0629.dll
