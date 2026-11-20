# AGENTS.md

TrueAspect is a Jellyfin 12 plugin (C#, `net10.0`) that measures the real picture aspect ratio of movies and episodes with ffmpeg `cropdetect`, stores the results, and posts a Home Assistant webhook when playback starts. The repo also ships a Home Assistant blueprint that turns those webhooks into projector lens memory changes.

## Precedence

These instructions apply to everything in this repository and override user-level instructions (such as `~/.claude/CLAUDE.md`) wherever the two conflict. User-level preferences that don't conflict, like output style and shell tooling, still apply.

## Project map

- `Jellyfin.Plugin.TrueAspect/`
  - `Plugin.cs`: plugin entry point, GUID, config page registration, and config normalization on load and save
  - `PluginServiceRegistrator.cs`: DI wiring for everything below
  - `Configuration/`: `PluginConfiguration` (settings, defaults, `Normalize()` clamps) and `configPage.html` (the embedded dashboard page)
  - `Detection/AspectRatioDetector.cs`: ffprobe and ffmpeg `cropdetect`, the scan concurrency gate
  - `Services/`: `PlaybackNotifier` (webhook on `PlaybackStart`), `ScanQueueService` (delayed scans of new, rescanned and unscanned played items), payload records
  - `ScheduledTasks/DetectAspectRatiosTask.cs`: the daily scan
  - `Storage/`: `AspectRatioStore` (`ratios.json`), `AspectRatioEntry`, `RatioSource`
  - `Api/`: `AspectRatioController` (`/TrueAspect/*`) and its request and response records
- `homeassistant/blueprints/`: Home Assistant automation blueprint
- `scripts/build.sh`, `scripts/install.sh`: local build and Docker install. `scripts/debug-install.sh` copies a debug build into a source-built server's data folder
- `build.yaml`: plugin metadata read by `scripts/build.sh` and jprm
- `.run/` (Rider) and `.vscode/` (VS Code): debug setup from the plugin template. It runs a source build of Jellyfin from `../jellyfin` and `../jellyfin-web` with the plugin copied into `~/.local/share/jellyfin/plugins/TrueAspect`
- `.github/workflows/build.yml` (pushes to `main` and PRs), `release.yml` (`v*` tags)

## Commands

```sh
dotnet build Jellyfin.Plugin.TrueAspect -c Release   # must end with 0 warnings, 0 errors
scripts/build.sh                                     # dist/TrueAspect_<version>/
scripts/install.sh --restart                         # into the JELLYFIN_CONTAINER container (default jellyfin)
```

There is no test project yet. For changes to `configPage.html`, extract the `<script>` block and run `node --check` on it.

Releasing: bump `version` and `changelog` in `build.yaml`, commit, then push a tag `v<version>` using the full four-part version (`v1.0.1.0`). The release workflow refuses tags that don't match `build.yaml`.

## Code rules

- The build treats warnings as errors with `AnalysisMode=AllEnabledByDefault`, StyleCop, SerilogAnalyzer and MultithreadingAnalyzer. Rule severities come from `.editorconfig`, which is the Jellyfin plugin template's plus a TrueAspect section at the end.
- Fix analyzer findings in code. Don't relax a rule in `.editorconfig` to get a build through. When a rule genuinely conflicts with what Jellyfin requires, suppress it on that member with `[SuppressMessage(..., Justification = "...")]`, as `PluginConfiguration.AllowedDevices` does.
- No XML documentation comments (`///`). The documentation rules are switched off in `.editorconfig` on purpose.
- No other comments by default. Only explain *why* something exists when an external constraint drives it, such as Jellyfin behaviour or an upstream quirk.
- One type per file, named after the type.
- Log through `[LoggerMessage]` source-generated partial methods.
- Long-running work goes in hosted services registered in `PluginServiceRegistrator`. The `Plugin` class must never be a hosted service.

## Jellyfin constraints

- `Jellyfin.Controller` and `Jellyfin.Model` stay on 12.0.0 with `ExcludeAssets="runtime"`, so the plugin loads on any 12.x server. Raising them raises the minimum server version, so only do it together with `targetAbi` in `build.yaml`, and never through automated dependency bumps.
- The plugin GUID `b3512314-ef01-4cde-8b1b-71b3ef6046a0` never changes. Two plugin folders with the same GUID both load, and the dashboard lists only one of them.
- Results live in `<jellyfin data>/trueaspect/ratios.json`, outside the versioned plugin folder so upgrades keep them. Keep the format backward compatible: it is a JSON list of `AspectRatioEntry`.
- Adding a setting means: the property and default in `PluginConfiguration`, a clamp in `Normalize()`, the field in `configPage.html` (including the `numberFields`, `checkFields` or `textFields` list), and the README if users see it. Defaults must preserve existing behaviour.

## Config page gotchas

- Jellyfin replaces `${...}` in plugin pages with translation strings, so JavaScript template literals break. Concatenate strings instead.
- Jellyfin's element styles override the `hidden` attribute. The page's scoped `[hidden] { display: none !important; }` rule fixes that; keep it.
- `emby-textarea` isn't upgraded on plugin pages. `is="emby-checkbox"` markup inserted with `innerHTML` is.
- Scope all CSS under `#TrueAspectConfigPage`.

## Documentation

- Write the README in a natural, human voice, and don't add prose that walks through what a code block does.
- Never publish real webhook IDs, API keys, household device names or user names. Use placeholders such as `jellyfin-playback-0123456789abcdef` and the user `jimothy`.
