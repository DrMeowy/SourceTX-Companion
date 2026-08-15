@echo off
setlocal
echo ===================================================
echo   SourceTX Companion App - Build Script (.NET/WPF)
echo ===================================================
echo.

set MSBUILD="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if not exist %MSBUILD% (
    echo [ERROR] MSBuild was not found at %MSBUILD%
    pause
    exit /b 1
)

echo [BUILD] Compiling SourceTX Companion Release executable...
%MSBUILD% "%~dp0SourceTXCompanion.csproj" /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal

if %ERRORLEVEL% equ 0 (
    copy /y "%~dp0bin\Release\SourceTXCompanion.exe" "%~dp0SourceTXCompanion.exe" >nul 2>nul
    echo.
    echo ===================================================
    echo  [SUCCESS] Build completed successfully!
    echo  Output binary: %~dp0bin\Release\SourceTXCompanion.exe
    echo ===================================================
) else (
    echo.
    echo [ERROR] Build failed. Review compiler messages above.
)

pause
