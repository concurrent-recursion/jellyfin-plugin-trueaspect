using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.ScheduledTasks;

public sealed partial class DetectAspectRatiosTask : IScheduledTask
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

        var stale = _store.GetAll()
            .Where(e => e.Source != RatioSource.Manual && _libraryManager.GetItemById(e.ItemId) is null)
            .Select(e => e.ItemId)
            .ToHashSet();
        var pruned = _store.RemoveWhere(e => stale.Contains(e.ItemId) && e.Source != RatioSource.Manual);
        if (pruned > 0)
        {
            LogPruned(pruned);
        }

        var todo = items.Where(i => _store.Get(i.Id) is null).ToList();
        LogCandidates(items.Count, todo.Count);

        var done = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = config.MaxConcurrentScans, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(todo, options, async (item, ct) =>
        {
            try
            {
                await _detector.DetectAsync(item, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDetectionFailed(ex, item.Name);
            }

            progress.Report(100.0 * Interlocked.Increment(ref done) / todo.Count);
        }).ConfigureAwait(false);

        progress.Report(100);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} entries for items no longer in the library")]
    private partial void LogPruned(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Total} candidate items, {Todo} need scanning")]
    private partial void LogCandidates(int total, int todo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Aspect ratio detection failed for {Name}")]
    private partial void LogDetectionFailed(Exception ex, string name);
}
