using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TrueAspect.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string HomeAssistantUrl { get; set; } = "http://host.docker.internal:8123";

    public string WebhookId { get; set; } = string.Empty;

    public bool ScanMovies { get; set; } = true;

    public bool ScanEpisodes { get; set; }

    public bool ScanOnItemAdded { get; set; } = true;

    public int SampleCount { get; set; } = 12;

    public int FramesPerSample { get; set; } = 8;

    public double BlackLimit { get; set; } = 0.1;

    public string? GetWebhookUrl()
    {
        var baseUrl = HomeAssistantUrl.Trim().TrimEnd('/');
        var id = WebhookId.Trim();
        return baseUrl.Length == 0 || id.Length == 0
            ? null
            : $"{baseUrl}/api/webhook/{Uri.EscapeDataString(id)}";
    }
}
