using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TrueAspect.Storage;

[JsonConverter(typeof(JsonStringEnumConverter<RatioSource>))]
public enum RatioSource
{
    Cropdetect,
    Stream,
    Manual,
    Failed,
}
