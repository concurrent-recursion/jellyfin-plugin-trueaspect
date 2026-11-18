#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
container="${JELLYFIN_CONTAINER:-jellyfin}"
build="$(ls -d "$root"/dist/TrueAspect_* 2>/dev/null | sort -V | tail -1)"
[[ -n "$build" ]] || { echo "Nothing built yet; run scripts/build.sh" >&2; exit 1; }
name="$(basename "$build")"
owner="$(docker inspect "$container" --format '{{.Config.User}}')"

docker exec -u 0 "$container" sh -c 'rm -rf /config/plugins/TrueAspect_*'
docker cp "$build" "$container:/config/plugins/$name"
docker exec -u 0 "$container" chown -R "${owner:-0:0}" "/config/plugins/$name"
echo "Installed $name into $container"

if [[ "${1:-}" == "--restart" ]]; then
  docker restart "$container"
else
  echo "Restart Jellyfin to load it: docker restart $container"
fi
