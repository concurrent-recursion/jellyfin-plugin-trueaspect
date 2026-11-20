using Jellyfin.Plugin.TrueAspect.Storage;

namespace Jellyfin.Plugin.TrueAspect.Api;

public sealed record RatioView(Guid ItemId, string Name, double? Ratio, RatioSource Source, DateTimeOffset UpdatedAt, string? Library);
