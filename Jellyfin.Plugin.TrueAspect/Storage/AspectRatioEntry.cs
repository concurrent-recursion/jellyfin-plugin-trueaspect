namespace Jellyfin.Plugin.TrueAspect.Storage;

public sealed record AspectRatioEntry(
    Guid ItemId,
    string Name,
    double? Ratio,
    RatioSource Source,
    DateTimeOffset UpdatedAt);
