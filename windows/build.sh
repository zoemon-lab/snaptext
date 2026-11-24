#!/bin/bash
# Mac から Windows 用の SnapText.exe を作る（.NET 8 SDK が必要）
set -e
cd "$(dirname "$0")"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
rm -rf dist
"$DOTNET" publish SnapText.csproj -c Release -o dist
rm -f dist/*.pdb
echo "Done: $(pwd)/dist/SnapText.exe"
