using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Services;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TrueAspect;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AspectRatioStore>();
        serviceCollection.AddSingleton<AspectRatioDetector>();
        serviceCollection.AddSingleton<ScanQueueService>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<ScanQueueService>());
        serviceCollection.AddSingleton<PlaybackNotifier>();
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<PlaybackNotifier>());
    }
}
