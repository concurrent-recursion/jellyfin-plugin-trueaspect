using System.ComponentModel.DataAnnotations;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueAspect.Detection;
using Jellyfin.Plugin.TrueAspect.Services;
using Jellyfin.Plugin.TrueAspect.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TrueAspect.Api;

[ApiController]
[Route("TrueAspect")]
public class AspectRatioController : ControllerBase
{
    private readonly AspectRatioStore _store;
    private readonly ScanQueue _scanQueue;
    private readonly PlaybackNotifier _notifier;
    private readonly ILibraryManager _libraryManager;

    public AspectRatioController(AspectRatioStore store, ScanQueue scanQueue, PlaybackNotifier notifier, ILibraryManager libraryManager)
    {
        _store = store;
        _scanQueue = scanQueue;
        _notifier = notifier;
        _libraryManager = libraryManager;
    }

    [HttpGet("Ratios")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<IReadOnlyList<RatioView>> GetAll()
    {
        var libraries = LibraryByItem();
        return Ok(_store.GetAll()
            .Select(e => new RatioView(e.ItemId, e.Name, e.Ratio, e.Source, e.UpdatedAt, libraries.GetValueOrDefault(e.ItemId)))
            .ToList());
    }

    private Dictionary<Guid, string> LibraryByItem()
    {
        var map = new Dictionary<Guid, string>();
        foreach (var library in _libraryManager.GetVirtualFolders())
        {
            if (!Guid.TryParse(library.ItemId, out var libraryId))
            {
                continue;
            }

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = libraryId,
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
                IsVirtualItem = false,
            });
            foreach (var item in items)
            {
                map.TryAdd(item.Id, library.Name);
            }
        }

        return map;
    }

    [HttpGet("Ratios/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AspectRatioEntry> Get([FromRoute] Guid itemId) =>
        _store.Get(itemId) is { } entry ? Ok(entry) : NotFound();

    [HttpPut("Ratios/{itemId}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AspectRatioEntry> SetManual([FromRoute] Guid itemId, [FromBody] ManualRatioRequest request)
    {
        if (_libraryManager.GetItemById(itemId) is not { } item)
        {
            return NotFound();
        }

        var entry = new AspectRatioEntry(item.Id, AspectRatioDetector.DisplayName(item), Math.Round(request.Ratio, 2), RatioSource.Manual, DateTimeOffset.UtcNow);
        _store.Set(entry);
        return Ok(entry);
    }

    [HttpDelete("Ratios/{itemId}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult Rescan([FromRoute] Guid itemId)
    {
        _store.Remove(itemId);
        if (_libraryManager.GetItemById(itemId) is Video)
        {
            _scanQueue.Enqueue(itemId);
        }

        return NoContent();
    }

    [HttpPost("RetryFailed")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<int> RetryFailed() => Ok(_store.RemoveWhere(e => e.Source == RatioSource.Failed));

    [HttpPost("TestWebhook")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestWebhookResult>> TestWebhook([FromQuery] Guid? itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        var url = config?.GetWebhookUrl();
        if (config is null || url is null)
        {
            return Ok(new TestWebhookResult(false, "Set the Home Assistant URL and webhook ID, then save.", null, null, null));
        }

        var item = itemId is { } id ? _libraryManager.GetItemById(id) : null;
        var payload = item is not null
            ? _notifier.BuildPayload(item, "TrueAspect test", "TrueAspect", null, "Test")
            : new PlaybackPayload("Test", "TrueAspect test", "TrueAspect", null, Guid.Empty.ToString("N"), "Movie", "Test", 2.39, "manual", 1.78);

        string message;
        int? status = null;
        var success = false;
        try
        {
            var response = await _notifier.SendAsync(payload, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                message = "Set the Home Assistant URL and webhook ID, then save.";
            }
            else
            {
                status = response.StatusCode;
                success = response.IsSuccess;
                var answer = $"{response.StatusCode} {response.ReasonPhrase}".TrimEnd();
                message = success ? $"Home Assistant accepted the test ({answer})." : $"Home Assistant answered {answer}.";
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            message = $"No answer within {config.WebhookTimeoutSeconds} seconds.";
        }
        catch (HttpRequestException ex)
        {
            message = $"Could not connect: {ex.Message}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or UriFormatException or NotSupportedException)
        {
            message = "The Home Assistant URL is not valid.";
        }

        return Ok(new TestWebhookResult(success, message, url, status, payload));
    }
}

public sealed record RatioView(Guid ItemId, string Name, double? Ratio, RatioSource Source, DateTimeOffset UpdatedAt, string? Library);

public sealed record TestWebhookResult(bool Success, string Message, string? Url, int? StatusCode, PlaybackPayload? Payload);

public sealed class ManualRatioRequest
{
    [Range(1.0, 4.0)]
    public double Ratio { get; set; }
}
