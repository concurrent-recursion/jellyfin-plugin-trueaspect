# TrueAspect

A Jellyfin plugin that works out the real picture aspect ratio of your movies and tells something else about it when playback starts. It was built to drive projector lens memories from Home Assistant, but the webhook is generic.

The file's own aspect ratio isn't good enough for that. A 2.39:1 film on a UHD disc is stored as a 3840x2160 frame with black bars baked in, so Jellyfin reports it as 16:9. TrueAspect runs ffmpeg's `cropdetect` at a dozen points through each movie and takes the median, which shrugs off dark scenes and the occasional IMAX shot. Files already cropped wider than 2:1 are taken at face value.

## What it does

- **Detects** ratios with Jellyfin's bundled ffmpeg, either from the *Detect aspect ratios* scheduled task (daily at 03:00 by default) or automatically a couple of minutes after a new item is added.
- **Stores** results in `<jellyfin data>/trueaspect/ratios.json` (`/config/data/trueaspect/ratios.json` in the official Docker image). That sits outside the versioned plugin folder so upgrades keep it, and metadata refreshes, NFO rewrites and tag changes never touch it.
- **Pushes** a Home Assistant webhook every time playback starts. By default that's every device; list device names under *Allowed devices* to post only those. The payload carries the device name either way, so the automation can filter too:

```json
{
  "event": "PlaybackStart",
  "device": "Home Theater",
  "client": "Jellyfin Android TV",
  "user": "brad",
  "item_id": "3f9c0d6e5a2b4c1d8e7f6a5b4c3d2e1f",
  "item_type": "Movie",
  "name": "Blade Runner 2049 (2017)",
  "ratio": 2.4,
  "ratio_source": "cropdetect",
  "stream_ratio": 1.78,
  "session_id": "8e1f0c2a9b7d4e3fa6c5b4a3928170fe"
}
```

`ratio_source` is `cropdetect`, `stream`, `manual`, `stream_fallback` (not scanned yet, so it falls back to the file's resolution and queues a scan), or `unknown`. `session_id` is the Jellyfin session that started playback, which you can pass to `POST /Sessions/{id}/Message` to show a message on that client.

- **Lets you correct it.** The plugin page lists every result with a filter, a per-item override, and a rescan button. Manual values are never overwritten.

## Installing

In Jellyfin, go to **Dashboard → Plugins → Repositories**, add this repository URL, then install TrueAspect from the catalog and restart the server.

```
https://concurrent-recursion.github.io/jellyfin-plugin-trueaspect/manifest.json
```

It needs Jellyfin 12.x.

## Building

You don't need the .NET SDK installed; the build script falls back to the official SDK container. `scripts/install.sh` copies the build into a Jellyfin Docker container (`JELLYFIN_CONTAINER`, default `jellyfin`).

```sh
scripts/build.sh
scripts/install.sh --restart
```

The plugin's name, version, `targetAbi`, changelog and image all live in `build.yaml`, which both `scripts/build.sh` and the release workflow read. It references the Jellyfin 12.0.0 packages and declares `targetAbi` 12.0.0.0, so it loads on any 12.x server. To target something else, change `targetAbi` and bump the package versions in the `.csproj` to match.

## Releasing

Bump `version` and `changelog` in `build.yaml`, commit, then push a matching tag:

```sh
git tag v1.0.1.0
git push origin v1.0.1.0
```

The release workflow builds the plugin with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager), attaches the zip to a GitHub release, and adds the version to `manifest.json` on the `gh-pages` branch. It refuses to run if the tag doesn't match `build.yaml`.

## API

All endpoints need an admin token except reading a single item.

| Method | Path | |
|---|---|---|
| GET | `/TrueAspect/Ratios` | every result |
| GET | `/TrueAspect/Ratios/{itemId}` | one item |
| PUT | `/TrueAspect/Ratios/{itemId}` | `{"Ratio": 2.39}` sets a manual value |
| DELETE | `/TrueAspect/Ratios/{itemId}` | forget the result and rescan |
| POST | `/TrueAspect/RetryFailed` | clear failed results so the task retries them |
| POST | `/TrueAspect/TestWebhook?itemId=` | send a test payload (`"event": "Test"`) |

## Home Assistant

On the plugin page, set the Home Assistant URL as Jellyfin sees it (`http://host.docker.internal:8123` when Jellyfin runs in Docker on the same host) and the `webhook_id` of your automation's trigger. The plugin posts to `<url>/api/webhook/<id>`.

```yaml
triggers:
  - trigger: webhook
    webhook_id: jellyfin-playback-xxxxxxxx
    allowed_methods: [POST]
    local_only: true
conditions:
  - condition: template
    value_template: >-
      {{ trigger.json.event == 'PlaybackStart'
         and trigger.json.device in ['Home Theater']
         and trigger.json.ratio | is_number }}
```

Pick a lens memory from `trigger.json.ratio`. The usual rule is the widest memory that's no wider than the movie, with a little tolerance so 2.35 encodes land on a 2.40 memory.

## License

GPL-3.0. See [LICENSE](LICENSE).
