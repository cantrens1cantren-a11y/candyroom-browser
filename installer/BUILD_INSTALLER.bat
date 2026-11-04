@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 goto no_sdk
where powershell >nul 2>&1
if errorlevel 1 goto no_powershell

echo Building Candyroom Browser...
if exist "browser\publish" rmdir /s /q "browser\publish"
dotnet publish "browser\Candyroom.csproj" -c Release -r win-x64 --self-contained true -o "browser\publish"
if errorlevel 1 goto failed

echo Packing browser files...
if exist "installer\payload.zip" del /q "installer\payload.zip"
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Compress-Archive -Path '.\browser\publish\*' -DestinationPath '.\installer\payload.zip' -CompressionLevel Optimal -Force"
if errorlevel 1 goto failed

echo Building installer...
dotnet publish "installer\CandyroomSetup.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "setup-output"
if errorlevel 1 goto failed
if not exist "setup-output\CandyroomSetup.exe" goto failed

echo.
echo Done! Installer: setup-output\CandyroomSetup.exe
echo You can distribute this EXE. WebView2 Runtime is required on the destination PC.
pause
exit /b 0
:no_sdk
echo .NET 8 SDK not found: https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1
:no_powershell
echo Windows PowerShell is required to create the installation archive.
pause
exit /b 1
:failed
echo.
echo Build failed. Copy and share all error messages above.
pause
exit /b 1
