using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.Storage;

public sealed partial class AspectRatioStore(IApplicationPaths applicationPaths, ILogger<AspectRatioStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Lock _lock = new();

    // The plugin's own DataFolderPath lives inside the versioned plugin directory, which is replaced on every upgrade.
    public string FilePath { get; } = Path.Combine(applicationPaths.DataPath, "trueaspect", "ratios.json");

    private Dictionary<Guid, AspectRatioEntry> Entries => field ??= Load();

    public AspectRatioEntry? Get(Guid itemId)
    {
        lock (_lock)
        {
            return Entries.GetValueOrDefault(itemId);
        }
    }

    public IReadOnlyList<AspectRatioEntry> GetAll()
    {
        lock (_lock)
        {
            return Entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void Set(AspectRatioEntry entry)
    {
        lock (_lock)
        {
            Entries[entry.ItemId] = entry;
            Save();
        }
    }

    public AspectRatioEntry GetOrAdd(AspectRatioEntry entry)
    {
        lock (_lock)
        {
            if (Entries.TryGetValue(entry.ItemId, out var existing))
            {
                return existing;
            }

            Entries[entry.ItemId] = entry;
            Save();
            return entry;
        }
    }

    public bool Remove(Guid itemId)
    {
        lock (_lock)
        {
            var removed = Entries.Remove(itemId);
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    public int RemoveWhere(Func<AspectRatioEntry, bool> predicate)
    {
        lock (_lock)
        {
            var ids = Entries.Values.Where(predicate).Select(e => e.ItemId).ToList();
            foreach (var id in ids)
            {
                Entries.Remove(id);
            }

            if (ids.Count > 0)
            {
                Save();
            }

            return ids.Count;
        }
    }

    private Dictionary<Guid, AspectRatioEntry> Load()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<Guid, AspectRatioEntry>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<AspectRatioEntry>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            return list.ToDictionary(e => e.ItemId);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            LogLoadFailed(logger, ex, FilePath);
            File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
            return new Dictionary<Guid, AspectRatioEntry>();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Entries.Values.ToList(), JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not read {Path}; starting with an empty store")]
    private static partial void LogLoadFailed(ILogger logger, Exception ex, string path);
}
