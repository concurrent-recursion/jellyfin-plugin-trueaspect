using System.Text.Json;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueAspect.Storage;

[JsonConverter(typeof(JsonStringEnumConverter<RatioSource>))]
public enum RatioSource
{
    Cropdetect,
    Stream,
    Manual,
    Failed,
}

public sealed record AspectRatioEntry(
    Guid ItemId,
    string Name,
    double? Ratio,
    RatioSource Source,
    DateTimeOffset UpdatedAt);

public sealed class AspectRatioStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<AspectRatioStore> _logger;
    private readonly Lock _lock = new();
    private Dictionary<Guid, AspectRatioEntry>? _entries;

    public AspectRatioStore(IApplicationPaths applicationPaths, ILogger<AspectRatioStore> logger)
    {
        _logger = logger;
        // The plugin's own DataFolderPath lives inside the versioned plugin directory, which is replaced on every upgrade.
        FilePath = Path.Combine(applicationPaths.DataPath, "trueaspect", "ratios.json");
    }

    public string FilePath { get; }

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

    private Dictionary<Guid, AspectRatioEntry> Entries => _entries ??= Load();

    private Dictionary<Guid, AspectRatioEntry> Load()
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return new Dictionary<Guid, AspectRatioEntry>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<AspectRatioEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
            return list.ToDictionary(e => e.ItemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read {Path}; starting with an empty store", path);
            File.Copy(path, path + ".corrupt", overwrite: true);
            return new Dictionary<Guid, AspectRatioEntry>();
        }
    }

    private void Save()
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Entries.Values.ToList(), JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
