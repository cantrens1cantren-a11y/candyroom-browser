@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 goto no_sdk

echo Building Candyroom Browser for Windows x64...
echo Internet is needed to download dependencies.
dotnet publish "Candyroom.csproj" -c Release -r win-x64 --self-contained true -o "publish"
if errorlevel 1 goto failed

echo.
echo Ready! Run publish\Candyroom.exe
pause
exit /b 0
:no_sdk
echo .NET SDK not found. Install the .NET 8 SDK:
echo https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1
:failed
echo.
echo Build failed. Copy and share the error messages above.
pause
exit /b 1
