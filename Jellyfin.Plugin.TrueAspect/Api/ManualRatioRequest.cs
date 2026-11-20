using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.TrueAspect.Api;

public sealed class ManualRatioRequest
{
    [Range(1.0, 4.0)]
    public double Ratio { get; set; }
}
