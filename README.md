# TrueAspect

A Jellyfin plugin that works out the real picture aspect ratio of your movies and tells Home Assistant about it when playback starts, so your projector can switch to the right lens memory on its own.

## Why this exists

A constant image height setup is lovely right up until you have to use it. With a 2.40:1 scope screen, every movie needs its own zoom: scope films fill the screen edge to edge, while flat 1.85 and 16:9 films sit in the middle at the same height. Get it wrong and the picture either spills off the sides of the screen or sits there letterboxed, with a fat black frame all the way round.

The hard part is knowing which one you've got. Jellyfin can't tell you, because almost every scope film is stored as a 16:9 frame with the black bars baked in, so it looks the same as everything else. The ratio on the box or on IMDb isn't reliable either. Remasters get reframed, some films switch ratios for IMAX scenes, and plenty of 2.20 and 2.00 releases don't fit any neat category. So movie night goes like this: press play, squint at the picture, find the projector remote, cycle through lens memories until the bars disappear, and get the family settled again.

TrueAspect does that for you. It measures the real ratio of every movie ahead of time. When anyone presses play, it tells Home Assistant that something is playing, on which device, and how wide the picture actually is. An automation picks the matching lens memory, and the lens is usually in place before the studio logo is over.

## What it does

A 2.39:1 film on a UHD disc is stored as a 3840x2160 frame with black bars baked in, so Jellyfin reports it as 16:9. TrueAspect runs ffmpeg's `cropdetect` at a dozen points through each movie and combines the results, which shrugs off dark scenes and the occasional IMAX shot. Files already wider than 2:1 are taken at face value (the threshold is configurable).

- **Detects** ratios with Jellyfin's bundled ffmpeg, either from the *Detect aspect ratios* scheduled task (daily at 03:00 by default) or automatically a couple of minutes after a new item is added.
- **Stores** results in `<jellyfin data>/trueaspect/ratios.json` (`/config/data/trueaspect/ratios.json` in the official Docker image). That sits outside the versioned plugin folder so upgrades keep it, and metadata refreshes, NFO rewrites and tag changes never touch it.
- **Pushes** a Home Assistant webhook every time playback starts. By default that's every device; list device names under *Allowed devices* to post only those. The payload carries the device name either way, so the automation can filter too:

```json
{
  "event": "PlaybackStart",
  "device": "Home Theater",
  "client": "Jellyfin Android TV",
  "user": "jimothy",
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

## What about Christopher Nolan or Wes Anderson?

Some directors treat aspect ratio as more of a suggestion. Nolan opens the frame up to full IMAX height whenever something expensive happens on screen, and Wes Anderson changes ratio whenever the story changes decade. A lens can only be in one place at a time, so somebody has to lose.

TrueAspect settles it by majority vote. It measures the black bars at a dozen points through the film and takes the median, so whichever framing covers most of the runtime wins and the occasional IMAX showpiece gets outvoted. *The Dark Knight* has about half an hour of IMAX in a two-and-a-half-hour film, so it comes out at 2.40 and the lens zooms out for scope, as it should.

Majority rule cuts both ways, though. Most of *Dunkirk* was shot in IMAX, so on disc the 16:9 scenes outnumber the 2.20 ones and TrueAspect calls it 1.78. That's arguably right for a constant image height screen, since nothing gets cropped. But if you'd rather watch the beach in scope and let the IMAX shots spill over, type 2.40 into the Override box on the plugin page. Manual values are never rescanned or overwritten, so Nolan stays overruled for good.

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

### Debugging in Rider or VS Code

Both editors can run a Jellyfin server from source with the plugin loaded, so you can set breakpoints in TrueAspect and step through it. The setup expects the server and web client checked out next to this repo and built once:

```sh
cd ..
git clone https://github.com/jellyfin/jellyfin.git
git clone https://github.com/jellyfin/jellyfin-web.git
dotnet build jellyfin/Jellyfin.Server
(cd jellyfin-web && npm ci && npm run build:development)
```

Check out the tag that matches the server you're targeting (for example `v12.4.0`) in both repos first. In Rider, debug the shared *Jellyfin with TrueAspect* run configuration; in VS Code, pick *Launch Jellyfin with TrueAspect* in the Run and Debug view. Either one builds the plugin, copies it into `~/.local/share/jellyfin/plugins/TrueAspect` (`%LOCALAPPDATA%\jellyfin` on Windows), and starts the server on port 8096. That data folder belongs to the debug server only, so your real server's settings and results aren't touched. Detection needs ffmpeg, so set its path under *Dashboard → Playback → Transcoding* if the debug server doesn't find one. If your checkouts are somewhere else, change the paths in `.run/Jellyfin with TrueAspect.run.xml` or `.vscode/settings.json`. Rider's configuration starts the `jellyfin` executable, so on Windows point it at `jellyfin.exe`. `scripts/debug-install.sh` does the build-and-copy step on its own and honours `JELLYFIN_DATA_DIR`.

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
| POST | `/TrueAspect/TestWebhook?itemId=` | send a test payload (`"event": "Test"`) and report `Success`, `Message` and `StatusCode` |

## Home Assistant

On the plugin page, set the Home Assistant URL as Jellyfin sees it (`http://host.docker.internal:8123` when Jellyfin runs in Docker on the same host) and the `webhook_id` of your automation's trigger. The plugin posts to `<url>/api/webhook/<id>`. *Send test webhook* checks the connection.

### Lens memory blueprint

The repo includes a Home Assistant blueprint that switches lens memories for you. It only reacts to the devices that play through the projector, and optionally only while the projector is on, so someone watching on a phone never moves the lens. It picks the widest memory that still fits the picture, with a little slack so a 2.30 encode still lands on a 2.35 memory. Anything narrower than every memory, like a 4:3 classic, falls back to the narrowest one and is pillarboxed.

[![Open your Home Assistant instance and show the blueprint import dialog with a specific blueprint pre-filled.](https://my.home-assistant.io/badges/blueprint_import.svg)](https://my.home-assistant.io/redirect/blueprint_import/?blueprint_url=https%3A%2F%2Fgithub.com%2Fconcurrent-recursion%2Fjellyfin-plugin-trueaspect%2Fblob%2Fmain%2Fhomeassistant%2Fblueprints%2Ftrueaspect_lens_memory.yaml)

Or import it by hand under **Settings → Automations & scenes → Blueprints → Import blueprint** with this URL:

```
https://github.com/concurrent-recursion/jellyfin-plugin-trueaspect/blob/main/homeassistant/blueprints/trueaspect_lens_memory.yaml
```

Then create an automation from it. It needs Home Assistant 2024.10 or later.

| Input | What it's for |
|---|---|
| Webhook ID | Must match the Webhook ID on the plugin page. Anyone on your network who knows it can trigger the automation, so make it hard to guess. |
| Projector devices | Jellyfin device names that play through the projector. Unlike the plugin's allowlist, these must match the `device` field exactly, capitals included. |
| Lens memories | Each ratio you have a memory for, mapped to its slot on the projector. Quote the ratios so YAML keeps `'2.00'` as written. |
| Ratio tolerance | How much wider than the picture a memory may be and still be chosen. `0.03` lets a 2.30 film use the 2.35 memory. Raise it if near-misses drop to a narrower memory; lower it if wide films spill off the screen. |
| Load lens memory | What to run to load a memory on your projector. |
| Projector power entity | Optional. When set, the lens only moves while this entity is in one of the on states. |
| Projector on states | The states that count as on. Defaults to `on` and `warmup`. |

*Load lens memory* is where your projector's own command goes, since JVC, Sony and Epson all do it differently. It can use `{{ memory }}` (the slot), `{{ memory_ratio }}` (that slot's ratio), `{{ ratio }}` (the movie's ratio) and the full payload in `trigger.json`. If you already have a script that takes a slot number, it's one action:

```yaml
- action: script.projector_load_lens_memory
  data:
    memory: "{{ memory }}"
```

When the blueprint changes, **Re-import blueprint** from its menu on the Blueprints page picks up the new version without touching your inputs.

## License

GPL-3.0. See [LICENSE](LICENSE).
