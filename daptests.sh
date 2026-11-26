#!/bin/sh
# Builds X16D (from ../BitMagic), the BASIC debug mode and its NuGet packages, then runs the end to end DAP tests.
set -e
MSBUILD="${MSBUILD:-/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/amd64/MSBuild.exe}"
"$MSBUILD" ../BitMagic/BitMagic.X16Debugger/X16D/X16D.csproj -restore -p:Configuration=Debug -p:Platform=x64 -v:q -nologo
dotnet pack src/BitMagic.X16Basic -c Debug -nologo -v q
"$MSBUILD" src/BitMagic.X16Basic.Debugger/BitMagic.X16Basic.Debugger.csproj -restore -t:Build,Pack -p:Configuration=Debug -p:Platform=x64 -v:q -nologo
dotnet test tests/BitMagic.X16Basic.DapTests -nologo "$@"
