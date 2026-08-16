#!/usr/bin/env bash

set -e

echo "==================================================="
echo "  Compilacion Multiplataforma SiFutbolNoCF"
echo "  Target: .NET 10.0"
echo "==================================================="
echo ""

# Comprobar si dotnet CLI está disponible
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: No se ha encontrado el comando 'dotnet'. Asegurate de tener instalado el SDK de .NET 10."
    exit 1
fi

echo "[+] Generando ejecutables independientes (Self-Contained / Single File)..."
echo "    Esto creara binarios listos para ejecutar sin necesidad de instalar .NET 10 en la maquina destino."
echo ""

PLATFORMS=("win-x64" "win-arm64" "osx-x64" "osx-arm64" "linux-x64" "linux-arm64")

for PLATFORM in "${PLATFORMS[@]}"; do
    echo "[$PLATFORM] Publicando..."
    if dotnet publish SiFutbolNoCF.csproj -c Release -r "$PLATFORM" --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o "build/$PLATFORM"; then
        echo "[$PLATFORM] OK: Publicado con exito."
    else
        echo "[$PLATFORM] ERROR: Fallo al publicar para esta plataforma."
    fi
    echo "---------------------------------------------------"
done

echo ""
echo "Compilacion terminada. Los binarios listos para distribuir estan en la carpeta ./build"
echo ""
