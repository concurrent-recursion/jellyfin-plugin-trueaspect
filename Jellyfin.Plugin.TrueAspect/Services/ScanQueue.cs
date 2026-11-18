using System.Threading.Channels;
using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.Services;

public sealed class ScanQueue : BackgroundService
{
    // Jellyfin raises ItemAdded before it has probed the file, so give the metadata refresh time to fill in streams and runtime.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMinutes(2);

    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly HashSet<Guid> _pending = [];
    private readonly Lock _pendingLock = new();
    private readonly ILibraryManager _libraryManager;
    private readonly AspectRatioDetector _detector;
    private readonly AspectRatioStore _store;
    private readonly ILogger<ScanQueue> _logger;

    public ScanQueue(ILibraryManager libraryManager, AspectRatioDetector detector, AspectRatioStore store, ILogger<ScanQueue> logger)
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

        _channel.Writer.TryWrite(itemId);
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
        await foreach (var itemId in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await Task.Delay(SettleDelay, stoppingToken).ConfigureAwait(false);
                if (_store.Get(itemId) is null && _libraryManager.GetItemById(itemId) is Video video)
                {
                    await _detector.DetectAsync(video, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aspect ratio detection failed for {ItemId}", itemId);
            }
            finally
            {
                lock (_pendingLock)
                {
                    _pending.Remove(itemId);
                }
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
}
