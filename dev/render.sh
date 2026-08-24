#!/usr/bin/env bash
# Render the chart offscreen to PNGs, no Playnite involved.
#
#   dev/render.sh <out-dir>
set -euo pipefail
cd "$(dirname "$0")/.."

MSBUILD="/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/MSBuild.exe"
OUT="${1:?usage: dev/render.sh <out-dir>}"

[ -x .tools/nuget.exe ] || { mkdir -p .tools; curl -sSL -o .tools/nuget.exe https://dist.nuget.org/win-x86-commandline/latest/nuget.exe; }
./.tools/nuget.exe restore packages.config -PackagesDirectory packages
"$MSBUILD" DevHarness/Harness.csproj -p:Configuration=Debug -v:m -nologo

mkdir -p "$OUT"
./DevHarness/bin/Debug/PlayniteCharts.DevHarness.exe "$OUT"
