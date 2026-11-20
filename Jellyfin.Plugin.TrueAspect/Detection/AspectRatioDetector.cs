using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.TrueAspect.Configuration;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.Detection;

public sealed partial class AspectRatioDetector
{
    private readonly IMediaEncoder _mediaEncoder;
    private readonly AspectRatioStore _store;
    private readonly ILogger<AspectRatioDetector> _logger;
    private readonly Lock _gateLock = new();
    private SemaphoreSlim _gate = new(1, 1);
    private int _gateSize = 1;

    public AspectRatioDetector(IMediaEncoder mediaEncoder, AspectRatioStore store, ILogger<AspectRatioDetector> logger)
    {
        _mediaEncoder = mediaEncoder;
        _store = store;
        _logger = logger;
    }

    public static bool IsCandidate(BaseItem item, PluginConfiguration config) =>
        item is Video { IsVirtualItem: false, IsPlaceHolder: false } video
        && !string.IsNullOrEmpty(video.Path)
        && video.VideoType == VideoType.VideoFile
        && ((config.ScanMovies && item is MediaBrowser.Controller.Entities.Movies.Movie)
            || (config.ScanEpisodes && item is MediaBrowser.Controller.Entities.TV.Episode));

    public static double? StreamRatio(BaseItem item)
    {
        var stream = (item as Video)?.GetDefaultVideoStream();
        if (stream is not { Width: > 0, Height: > 0 })
        {
            return null;
        }

        var storage = (double)stream.Width.Value / stream.Height.Value;
        var display = ParseRatio(stream.AspectRatio);
        var ratio = display is { } d && Math.Abs((d / storage) - 1) > 0.03 ? d : storage;
        return Math.Round(ratio, 2);
    }

    private static double? ParseRatio(string? value)
    {
        var parts = value?.Split(':');
        return parts is { Length: 2 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
            && num > 0 && den > 0
                ? num / den
                : null;
    }

    public async Task<AspectRatioEntry> DetectAsync(Video item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var gate = Gate(config.MaxConcurrentScans);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_store.Get(item.Id) is { } existing)
            {
                return existing;
            }

            var streamRatio = StreamRatio(item);
            double? ratio;
            RatioSource source;
            if (streamRatio >= config.AlreadyCroppedRatio)
            {
                ratio = streamRatio;
                source = RatioSource.Stream;
            }
            else
            {
                ratio = await CropDetectAsync(item, config, cancellationToken).ConfigureAwait(false);
                source = ratio is null ? RatioSource.Failed : RatioSource.Cropdetect;
            }

            var entry = _store.GetOrAdd(new AspectRatioEntry(item.Id, DisplayName(item), ratio, source, DateTimeOffset.UtcNow));
            LogDetected(entry.Name, entry.Ratio, entry.Source);
            return entry;
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim Gate(int size)
    {
        lock (_gateLock)
        {
            if (size != _gateSize)
            {
                _gate = new SemaphoreSlim(size, size);
                _gateSize = size;
            }

            return _gate;
        }
    }

    public static string DisplayName(BaseItem item) => item switch
    {
        MediaBrowser.Controller.Entities.TV.Episode { ParentIndexNumber: { } season, IndexNumber: { } number } ep =>
            $"{ep.SeriesName} S{season:00}E{number:00} - {ep.Name}",
        MediaBrowser.Controller.Entities.TV.Episode ep => $"{ep.SeriesName} - {ep.Name}",
        _ when item.ProductionYear is { } year => $"{item.Name} ({year})",
        _ => item.Name,
    };

    private async Task<double?> CropDetectAsync(Video item, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(config.FfmpegTimeoutSeconds);
        var probe = await ProbeAsync(item.Path, timeout, cancellationToken).ConfigureAwait(false);
        var duration = item.RunTimeTicks is > 0 ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : probe.Duration;
        if (duration is not { TotalSeconds: > 60 } || probe.Width <= 0 || probe.Height <= 0)
        {
            LogNoDuration(item.Name);
            return null;
        }

        var samples = config.SampleCount;
        var start = config.SkipStartPercent / 100;
        var span = Math.Max(0.1, 1 - start - (config.SkipEndPercent / 100));
        var limit = config.BlackLimit.ToString(CultureInfo.InvariantCulture);
        var tops = new List<int>(samples);
        var bottoms = new List<int>(samples);
        var lefts = new List<int>(samples);
        var rights = new List<int>(samples);
        for (var i = 1; i <= samples; i++)
        {
            var seconds = (int)(duration.Value.TotalSeconds * (start + (span * i / (samples + 1))));
            var stderr = await RunAsync(
                _mediaEncoder.EncoderPath,
                [
                    "-nostdin", "-hide_banner", "-ss", seconds.ToString(CultureInfo.InvariantCulture),
                    "-i", item.Path, "-map", "0:V:0",
                    "-vf", $"cropdetect=limit={limit}:round=2:reset=0",
                    "-frames:v", config.FramesPerSample.ToString(CultureInfo.InvariantCulture),
                    "-an", "-sn", "-f", "null", "-",
                ],
                readStdout: false,
                timeout,
                cancellationToken).ConfigureAwait(false);

            var match = CropRegex().Matches(stderr).LastOrDefault();
            if (match is not null
                && int.TryParse(match.Groups["w"].ValueSpan, CultureInfo.InvariantCulture, out var w)
                && int.TryParse(match.Groups["h"].ValueSpan, CultureInfo.InvariantCulture, out var h)
                && int.TryParse(match.Groups["x"].ValueSpan, CultureInfo.InvariantCulture, out var x)
                && int.TryParse(match.Groups["y"].ValueSpan, CultureInfo.InvariantCulture, out var y)
                && w > 0 && h > 0)
            {
                tops.Add(y);
                bottoms.Add(Math.Max(0, probe.Height - y - h));
                lefts.Add(x);
                rights.Add(Math.Max(0, probe.Width - x - w));
            }
        }

        if (tops.Count < samples / 2)
        {
            LogTooFewSamples(item.Name, tops.Count, samples);
            return null;
        }

        // Dark scenes only ever make a bar look thicker. Vertical bars use the median so brief open-matte/IMAX shots are
        // ignored; nothing legitimately narrows a pillarbox, so horizontal bars use a lower percentile to resist dark edges.
        var height = probe.Height - Bars(tops, bottoms, 0.5, probe.Height);
        var width = probe.Width - Bars(lefts, rights, 0.25, probe.Width);
        return width > 0 && height > 0 ? Math.Round(width * probe.PixelAspect / height, 2) : null;
    }

    // DVDs often have slightly uneven bars (e.g. 54/60 px), which are kept as-is. A large difference means darkness
    // inflated one side, so the thinner bar is mirrored.
    private static double Bars(List<int> first, List<int> second, double percentile, int size)
    {
        var a = Percentile(first, percentile);
        var b = Percentile(second, percentile);
        return Math.Abs(a - b) <= 0.02 * size ? a + b : 2 * Math.Min(a, b);
    }

    private static double Percentile(List<int> values, double p)
    {
        values.Sort();
        var pos = (values.Count - 1) * p;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return values[lo] + ((values[hi] - values[lo]) * (pos - lo));
    }

    private async Task<(TimeSpan? Duration, double PixelAspect, int Width, int Height)> ProbeAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stdout = await RunAsync(
            _mediaEncoder.ProbePath,
            ["-v", "error", "-select_streams", "V:0", "-show_entries", "stream=width,height,sample_aspect_ratio:format=duration", "-of", "json", path],
            readStdout: true,
            timeout,
            cancellationToken).ConfigureAwait(false);

        TimeSpan? duration = null;
        var pixelAspect = 1.0;
        int width = 0, height = 0;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("format", out var format)
                && format.TryGetProperty("duration", out var d)
                && double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                duration = TimeSpan.FromSeconds(seconds);
            }

            if (doc.RootElement.TryGetProperty("streams", out var streams) && streams.GetArrayLength() > 0)
            {
                var stream = streams[0];
                if (stream.TryGetProperty("sample_aspect_ratio", out var sar) && ParseRatio(sar.GetString()) is { } parsed)
                {
                    pixelAspect = parsed;
                }

                if (stream.TryGetProperty("width", out var w) && stream.TryGetProperty("height", out var h))
                {
                    width = w.GetInt32();
                    height = h.GetInt32();
                }
            }
        }
        catch (JsonException)
        {
            LogUnreadableProbe(path);
        }

        return (duration, pixelAspect, width, height);
    }

    private async Task<string> RunAsync(string fileName, IEnumerable<string> args, bool readStdout, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            LogTimedOut(Path.GetFileName(fileName));
            return string.Empty;
        }

        return readStdout ? await stdoutTask.ConfigureAwait(false) : await stderrTask.ConfigureAwait(false);
    }

    [GeneratedRegex(@"crop=(?<w>\d+):(?<h>\d+):(?<x>\d+):(?<y>\d+)")]
    private static partial Regex CropRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Name}: aspect ratio {Ratio} ({Source})")]
    private partial void LogDetected(string name, double? ratio, RatioSource source);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Name}: no usable duration or frame size, skipping cropdetect")]
    private partial void LogNoDuration(string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Name}: only {Count}/{Samples} cropdetect samples succeeded")]
    private partial void LogTooFewSamples(string name, int count, int samples);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ffprobe returned unreadable output for {Path}")]
    private partial void LogUnreadableProbe(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{File} timed out")]
    private partial void LogTimedOut(string file);
}
