namespace Jellyfin.Plugin.TrueAspect.Services;

public sealed record WebhookResponse(int StatusCode, string? ReasonPhrase, bool IsSuccess);
