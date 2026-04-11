#!/bin/bash
set -e

cd "$(dirname "$0")"

echo "[1/3] Agent fuer win-x64 bauen..."
dotnet publish agent/HackITSentry.Agent.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o agent/publish/win-x64 \
  --nologo -v quiet

echo "[2/3] Agent in Installer einbetten..."
mkdir -p installer/Resources
cp agent/publish/win-x64/HackITSentry.Agent.exe installer/Resources/

echo "[3/3] Installer bauen..."
dotnet publish installer/Installer.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o installer/publish \
  --nologo -v quiet

echo ""
echo "Fertig: installer/publish/HackITSentry-Setup.exe"
