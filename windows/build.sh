#!/bin/bash
# Builds DragShift.exe for Windows (on a Mac, Linux or Windows with the .NET SDK) after running the
# shared-logic tests. Result: build/windows/DragShift.exe (one file, nothing to install).
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec; fi
dotnet run --project tests/CoreTests -c Release
dotnet build src/DragShift -c Release -o ../build/windows --nologo -v quiet
rm -f ../build/windows/DragShift.exe.config
echo "Built $(cd ../build/windows && pwd)/DragShift.exe ($(du -k ../build/windows/DragShift.exe | cut -f1) KB)"
