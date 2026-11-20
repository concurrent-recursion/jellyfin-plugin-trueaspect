using System.Threading.Channels;
using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.Services;

public sealed partial class ScanQueueService : BackgroundService
{
    private readonly Channel<(Guid ItemId, DateTimeOffset Due)> _channel = Channel.CreateUnbounded<(Guid ItemId, DateTimeOffset Due)>();
    private readonly HashSet<Guid> _pending = [];
    private readonly Lock _pendingLock = new();
    private readonly ILibraryManager _libraryManager;
    private readonly AspectRatioDetector _detector;
    private readonly AspectRatioStore _store;
    private readonly ILogger<ScanQueueService> _logger;

    public ScanQueueService(ILibraryManager libraryManager, AspectRatioDetector detector, AspectRatioStore store, ILogger<ScanQueueService> logger)
    {
        _libraryManager = libraryManager;
        _detector = detector;
        _store = store;
        _logger = logger;
    }

    public void Enqueue(Guid itemId)
    {
        lock (_pendingLock)
        {
            if (!_pending.Add(itemId))
            {
                return;
            }
        }

        // Jellyfin raises ItemAdded before it has probed the file, so give the metadata refresh time to fill in streams and runtime.
        var delay = TimeSpan.FromSeconds(Plugin.Instance?.Configuration.QueueDelaySeconds ?? 120);
        _channel.Writer.TryWrite((itemId, DateTimeOffset.UtcNow + delay));
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new List<Task>();
        try
        {
            await foreach (var (itemId, due) in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                var wait = due - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
                }

                running.RemoveAll(t => t.IsCompleted);
                running.Add(ScanAsync(itemId, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private async Task ScanAsync(Guid itemId, CancellationToken stoppingToken)
    {
        try
        {
            if (_store.Get(itemId) is null && _libraryManager.GetItemById(itemId) is Video video)
            {
                await _detector.DetectAsync(video, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogDetectionFailed(ex, itemId);
        }
        finally
        {
            lock (_pendingLock)
            {
                _pending.Remove(itemId);
            }
        }
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is { ScanOnItemAdded: true } && AspectRatioDetector.IsCandidate(e.Item, config))
        {
            Enqueue(e.Item.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Aspect ratio detection failed for {ItemId}")]
    private partial void LogDetectionFailed(Exception ex, Guid itemId);
}
