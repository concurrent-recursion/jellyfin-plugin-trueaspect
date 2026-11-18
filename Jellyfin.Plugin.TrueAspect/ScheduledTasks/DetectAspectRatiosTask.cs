using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.ScheduledTasks;

public sealed class DetectAspectRatiosTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly AspectRatioDetector _detector;
    private readonly AspectRatioStore _store;
    private readonly ILogger<DetectAspectRatiosTask> _logger;

    public DetectAspectRatiosTask(
        ILibraryManager libraryManager,
        AspectRatioDetector detector,
        AspectRatioStore store,
        ILogger<DetectAspectRatiosTask> logger)
    {
        _libraryManager = libraryManager;
        _detector = detector;
        _store = store;
        _logger = logger;
    }

    public string Name => "Detect aspect ratios";

    public string Key => "TrueAspectDetect";

    public string Description => "Runs cropdetect on movies (and optionally episodes) that have not been scanned yet.";

    public string Category => "TrueAspect";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks },
    ];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var kinds = new List<BaseItemKind>();
        if (config.ScanMovies)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (config.ScanEpisodes)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        if (kinds.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = kinds.ToArray(),
                IsVirtualItem = false,
                Recursive = true,
            })
            .OfType<Video>()
            .Where(v => AspectRatioDetector.IsCandidate(v, config))
            .ToList();

        var live = items.Select(i => i.Id).ToHashSet();
        var pruned = _store.RemoveWhere(e => !live.Contains(e.ItemId) && e.Source != RatioSource.Manual);
        if (pruned > 0)
        {
            _logger.LogInformation("Removed {Count} entries for items no longer in the library", pruned);
        }

        var todo = items.Where(i => _store.Get(i.Id) is null).ToList();
        _logger.LogInformation("{Total} candidate items, {Todo} need scanning", items.Count, todo.Count);

        for (var i = 0; i < todo.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _detector.DetectAsync(todo[i], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Aspect ratio detection failed for {Name}", todo[i].Name);
            }

            progress.Report(100.0 * (i + 1) / todo.Count);
        }

        progress.Report(100);
    }
}
