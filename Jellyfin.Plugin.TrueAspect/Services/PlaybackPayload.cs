using System.Text.Json.Serialization;

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
