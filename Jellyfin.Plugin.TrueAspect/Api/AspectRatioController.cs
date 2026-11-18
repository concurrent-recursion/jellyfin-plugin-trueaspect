using System.ComponentModel.DataAnnotations;
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
[Authorize(Policy = "RequiresElevation")]
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
    public ActionResult<IReadOnlyList<AspectRatioEntry>> GetAll() => Ok(_store.GetAll());

    [HttpGet("Ratios/{itemId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AspectRatioEntry> Get([FromRoute] Guid itemId) =>
        _store.Get(itemId) is { } entry ? Ok(entry) : NotFound();

    [HttpPut("Ratios/{itemId}")]
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
    public ActionResult<int> RetryFailed() => Ok(_store.RemoveWhere(e => e.Source == RatioSource.Failed));

    [HttpPost("TestWebhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaybackPayload>> TestWebhook([FromQuery] Guid? itemId, CancellationToken cancellationToken)
    {
        var item = itemId is { } id ? _libraryManager.GetItemById(id) : null;
        var payload = item is not null
            ? _notifier.BuildPayload(item, "TrueAspect test", "TrueAspect", null, "Test")
            : new PlaybackPayload("Test", "TrueAspect test", "TrueAspect", null, Guid.Empty.ToString("N"), "Movie", "Test", 2.39, "manual", 1.78);

        try
        {
            return await _notifier.SendAsync(payload, cancellationToken).ConfigureAwait(false)
                ? Ok(payload)
                : BadRequest("Webhook URL is empty or the endpoint returned an error");
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

public sealed class ManualRatioRequest
{
    [Range(1.0, 4.0)]
    public double Ratio { get; set; }
}
