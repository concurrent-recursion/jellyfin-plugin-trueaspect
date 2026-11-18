#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cfg="$root/build.yaml"
field() { yq -r ".$1" "$cfg"; }

name="$(field name)"
version="$(field version)"
target_abi="${TARGET_ABI:-$(field targetAbi)}"
image="$(field image)"
out="$root/dist/${name}_${version}"
publish=(publish Jellyfin.Plugin.TrueAspect -c Release -o dist/publish "-p:Version=$version")

build() {
  if command -v dotnet >/dev/null 2>&1; then
    (cd "$root" && dotnet "${publish[@]}")
  else
    local nuget="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
    mkdir -p "$nuget"
    docker run --rm -u "$(id -u):$(id -g)" \
      -e HOME=/tmp -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_PACKAGES=/nuget \
      -v "$root":/src -v "$nuget":/nuget -w /src \
      mcr.microsoft.com/dotnet/sdk:10.0 \
      dotnet "${publish[@]}"
  fi
}

build
rm -rf "$out"
mkdir -p "$out"
yq -r '.artifacts[]' "$cfg" | while read -r artifact; do
  cp "$root/dist/publish/$artifact" "$out/"
done
cp "$root/$image" "$out/"

jq -n \
  --arg guid "$(field guid)" \
  --arg name "$name" \
  --arg version "$version" \
  --arg targetAbi "$target_abi" \
  --arg owner "$(field owner)" \
  --arg category "$(field category)" \
  --arg overview "$(field overview)" \
  --arg description "$(field description)" \
  --arg changelog "$(field changelog)" \
  --arg imagePath "$(basename "$image")" \
  --arg timestamp "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --argjson assemblies "$(yq -r '.artifacts[]' "$cfg" | jq -R . | jq -s .)" \
  '$ARGS.named + {status: "Active", autoUpdate: false}' > "$out/meta.json"

echo "Built $out"
