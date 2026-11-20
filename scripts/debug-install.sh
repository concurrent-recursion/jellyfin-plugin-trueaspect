#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
data="${JELLYFIN_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/jellyfin}"
plugin="$data/plugins/TrueAspect"
publish="$root/Jellyfin.Plugin.TrueAspect/bin/Debug/net10.0/publish"

dotnet publish "$root/Jellyfin.Plugin.TrueAspect/Jellyfin.Plugin.TrueAspect.csproj" \
  --configuration Debug /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary

mkdir -p "$plugin"
cp "$publish/Jellyfin.Plugin.TrueAspect.dll" "$publish/Jellyfin.Plugin.TrueAspect.pdb" "$plugin/"
echo "Copied debug build to $plugin"
