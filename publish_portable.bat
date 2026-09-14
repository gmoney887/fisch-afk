@echo off
title Building Portable FischMacroCS (Zero-Dependency Standalone)
echo ========================================================
echo   Building Fat Dad's Fisch AFK Pro - Portable Standalone
echo ========================================================
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-singlefile

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================================
    echo  SUCCESS! Standalone portable exe created:
    echo  publish-singlefile\FischMacroCS.exe
    echo.
    echo  This file runs on ANY 64-bit Windows PC with NO .NET 
    echo  runtime or SDK installation required!
    echo ========================================================
) else (
    echo.
    echo BUILD FAILED. Please check errors above.
)
pause
