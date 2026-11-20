using Jellyfin.Plugin.TrueAspect.Services;

namespace Jellyfin.Plugin.TrueAspect.Api;

public sealed record TestWebhookResult(bool Success, string Message, string? Url, int? StatusCode, PlaybackPayload? Payload);
