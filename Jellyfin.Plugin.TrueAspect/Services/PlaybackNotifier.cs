using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.Services;

public sealed record PlaybackPayload(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("client")] string? Client,
    [property: JsonPropertyName("user")] string? User,
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("item_type")] string ItemType,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ratio")] double? Ratio,
    [property: JsonPropertyName("ratio_source")] string RatioSource,
    [property: JsonPropertyName("stream_ratio")] double? StreamRatio,
    [property: JsonPropertyName("session_id")] string? SessionId = null);

public sealed partial class PlaybackNotifier : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AspectRatioStore _store;
    private readonly ScanQueue _scanQueue;
    private readonly ILogger<PlaybackNotifier> _logger;

    public PlaybackNotifier(
        ISessionManager sessionManager,
        IHttpClientFactory httpClientFactory,
        AspectRatioStore store,
        ScanQueue scanQueue,
        ILogger<PlaybackNotifier> logger)
    {
        _sessionManager = sessionManager;
        _httpClientFactory = httpClientFactory;
        _store = store;
        _scanQueue = scanQueue;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        return Task.CompletedTask;
    }

    public PlaybackPayload BuildPayload(BaseItem item, string? device, string? client, string? user, string eventName = "PlaybackStart", string? sessionId = null)
    {
        var entry = _store.Get(item.Id);
        var streamRatio = AspectRatioDetector.StreamRatio(item);
        var (ratio, source) = entry?.Ratio is { } stored
            ? (stored, entry.Source.ToString().ToLowerInvariant())
            : (streamRatio, streamRatio is null ? "unknown" : "stream_fallback");

        return new PlaybackPayload(
            eventName,
            device,
            client,
            user,
            item.Id.ToString("N"),
            item.GetType().Name,
            AspectRatioDetector.DisplayName(item),
            ratio,
            source,
            streamRatio,
            sessionId);
    }

    public async Task<bool> SendAsync(PlaybackPayload payload, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.GetWebhookUrl() is not { } url)
        {
            return false;
        }

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(config.WebhookTimeoutSeconds);
        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            LogWebhookStatus((int)response.StatusCode, payload.Name);
        }

        return response.IsSuccessStatusCode;
    }

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config is null || e.Item is not Video item)
            {
                return;
            }

            if (_store.Get(item.Id) is null && AspectRatioDetector.IsCandidate(item, config))
            {
                _scanQueue.Enqueue(item.Id);
            }

            if (config.GetWebhookUrl() is null)
            {
                return;
            }

            var device = e.DeviceName ?? e.Session?.DeviceName;
            if (!config.IsDeviceAllowed(device))
            {
                LogDeviceSkipped(device);
                return;
            }

            var payload = BuildPayload(item, device, e.ClientName ?? e.Session?.Client, e.Session?.UserName, sessionId: e.Session?.Id);
            LogPlaybackStarted(device, payload.Name, payload.Ratio, payload.RatioSource);
            await SendAsync(payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWebhookFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook returned {Status} for {Name}")]
    private partial void LogWebhookStatus(int status, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Device} started {Name}: ratio {Ratio} ({Source})")]
    private partial void LogPlaybackStarted(string? device, string name, double? ratio, string source);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Device} is not in the device allowlist, skipping webhook")]
    private partial void LogDeviceSkipped(string? device);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send playback webhook")]
    private partial void LogWebhookFailed(Exception ex);
}
