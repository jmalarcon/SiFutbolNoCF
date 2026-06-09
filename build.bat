@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Compilacion Multiplataforma SiFutbolNoCF
echo   Target: .NET 10.0
echo ===================================================
echo.

:: Comprobar si dotnet CLI está disponible
where dotnet >nul 2>&1
if !errorlevel! neq 0 (
    echo ERROR: No se ha encontrado el comando 'dotnet'. Asegurate de tener instalado el SDK de .NET 10.
    exit /b 1
)

echo [+] Generando ejecutables independientes (Self-Contained / Single File)...
echo     Esto creara binarios listos para ejecutar sin necesidad de instalar .NET 10 en la maquina destino.
echo.

set "PLATFORMS=win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64"

for %%P in (%PLATFORMS%) do (
    echo [%%P] Publicando...
    dotnet publish SiFutbolNoCF.csproj -c Release -r %%P --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o build\%%P
    if !errorlevel! equ 0 (
        echo [%%P] OK: Publicado con exito.
    ) else (
        echo [%%P] ERROR: Fallo al publicar para esta plataforma.
    )
    echo ---------------------------------------------------
)

echo.
echo Compilacion terminada. Los binarios listos para distribuir estan en la carpeta ./build
echo.
exit /b 0
