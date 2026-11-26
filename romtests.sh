#!/bin/sh
# Builds and runs the ROM reference tests (needs MSBuild for the native emulator core).
set -e
MSBUILD="${MSBUILD:-/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/amd64/MSBuild.exe}"
"$MSBUILD" tests/BitMagic.X16Basic.RomTests/BitMagic.X16Basic.RomTests.csproj -restore -p:Configuration=Debug -p:Platform=x64 -v:q -nologo
dotnet test tests/BitMagic.X16Basic.RomTests --no-build -p:Platform=x64 -nologo "$@"
