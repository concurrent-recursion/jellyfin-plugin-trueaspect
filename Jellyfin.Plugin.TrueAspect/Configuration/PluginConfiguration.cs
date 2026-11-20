using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TrueAspect.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string HomeAssistantUrl { get; set; } = "http://host.docker.internal:8123";

    public string WebhookId { get; set; } = string.Empty;

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Jellyfin serializes plugin configuration to XML and JSON, which need a settable array.")]
    public string[] AllowedDevices { get; set; } = [];

    public int WebhookTimeoutSeconds { get; set; } = 10;

    public bool ScanMovies { get; set; } = true;

    public bool ScanEpisodes { get; set; }

    public bool ScanOnItemAdded { get; set; } = true;

    public int QueueDelaySeconds { get; set; } = 120;

    public int MaxConcurrentScans { get; set; } = 1;

    public int SampleCount { get; set; } = 12;

    public int FramesPerSample { get; set; } = 8;

    public double BlackLimit { get; set; } = 0.1;

    public double AlreadyCroppedRatio { get; set; } = 2.0;

    public double SkipStartPercent { get; set; } = 5;

    public double SkipEndPercent { get; set; } = 5;

    public int FfmpegTimeoutSeconds { get; set; } = 120;

    public string? GetWebhookUrl()
    {
        var baseUrl = HomeAssistantUrl.Trim().TrimEnd('/');
        var id = WebhookId.Trim();
        return baseUrl.Length == 0 || id.Length == 0
            ? null
            : $"{baseUrl}/api/webhook/{Uri.EscapeDataString(id)}";
    }

    public bool IsDeviceAllowed(string? device) =>
        AllowedDevices.Length == 0
        || (device is not null && AllowedDevices.Contains(device.Trim(), StringComparer.OrdinalIgnoreCase));

    public void Normalize()
    {
        AllowedDevices = (AllowedDevices ?? [])
            .Select(d => d?.Trim() ?? string.Empty)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        WebhookTimeoutSeconds = Math.Clamp(WebhookTimeoutSeconds, 1, 60);
        QueueDelaySeconds = Math.Clamp(QueueDelaySeconds, 0, 3600);
        MaxConcurrentScans = Math.Clamp(MaxConcurrentScans, 1, 8);
        SampleCount = Math.Clamp(SampleCount, 3, 60);
        FramesPerSample = Math.Clamp(FramesPerSample, 2, 60);
        BlackLimit = Math.Clamp(BlackLimit, 0.01, 0.5);
        AlreadyCroppedRatio = Math.Clamp(AlreadyCroppedRatio, 1.8, 4.0);
        SkipStartPercent = Math.Clamp(SkipStartPercent, 0, 40);
        SkipEndPercent = Math.Clamp(SkipEndPercent, 0, 40);
        FfmpegTimeoutSeconds = Math.Clamp(FfmpegTimeoutSeconds, 10, 1800);
    }
}
