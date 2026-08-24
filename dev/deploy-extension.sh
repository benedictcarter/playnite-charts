#!/usr/bin/env bash
# Build the Charts extension and drop it into a Playnite instance.
#
#   dev/deploy-extension.sh              -> the installed Playnite at %APPDATA%\Playnite
#   dev/deploy-extension.sh <dir>        -> some other instance's Extensions dir
set -euo pipefail
cd "$(dirname "$0")/.."

MSBUILD="/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin/MSBuild.exe"
TARGET="${1:-$APPDATA/Playnite/Extensions}"

[ -x .tools/nuget.exe ] || { mkdir -p .tools; curl -sSL -o .tools/nuget.exe https://dist.nuget.org/win-x86-commandline/latest/nuget.exe; }
./.tools/nuget.exe restore packages.config -PackagesDirectory packages
"$MSBUILD" PlayniteCharts.csproj -p:Configuration=Debug -v:m -nologo

# Playnite holds the extension DLL open, so the copy fails with "Device or
# resource busy" while it runs. Ben's standing instruction: just close it.
RESTART=
if tasklist //FI "IMAGENAME eq Playnite.DesktopApp.exe" 2>/dev/null | grep -qi Playnite.DesktopApp.exe; then
    echo "closing running Playnite so the DLL can be replaced"
    RESTART=1
    # ask nicely first so Playnite flushes its LiteDB; force only if it lingers
    taskkill //IM Playnite.DesktopApp.exe >/dev/null 2>&1 || true
    for i in 1 2 3 4 5 6 7 8 9 10; do
        tasklist //FI "IMAGENAME eq Playnite.DesktopApp.exe" 2>/dev/null | grep -qi Playnite.DesktopApp.exe || break
        [ "$i" = 6 ] && taskkill //IM Playnite.DesktopApp.exe //F >/dev/null 2>&1 || true
        sleep 0.5
    done
fi

DST="$TARGET/PlayniteCharts"
mkdir -p "$DST"
cp bin/Debug/PlayniteCharts.dll "$DST/"
cp bin/Debug/PlayniteCharts.pdb "$DST/" 2>/dev/null || true
cp extension.yaml icon.png "$DST/"
echo "deployed -> $DST"

# it was running before we started; put it back, extension reloaded
if [ -n "$RESTART" ] && [ -x "$LOCALAPPDATA/Playnite/Playnite.DesktopApp.exe" ]; then
    "$LOCALAPPDATA/Playnite/Playnite.DesktopApp.exe" >/dev/null 2>&1 &
    echo "restarted Playnite"
fi
