using Microsoft.AspNetCore.Mvc;
using TodoApi.Dtos.Sync;
using TodoApi.Services.Sync;

namespace TodoApi.Controllers;

[Route("api/sync")]
[ApiController]
public class SyncController : ControllerBase
{
    private readonly ISyncService _syncService;
    private readonly ILogger<SyncController> _logger;

    public SyncController(ISyncService syncService, ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger sync from remote API (pull operation)
    /// </summary>
    [HttpPost("pull")]
    public async Task<ActionResult<SyncResultDto>> SyncFromRemote(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Manual sync from remote triggered via API");

            var result = await _syncService.SyncFromRemoteAsync(cancellationToken);

            return result.Success
                ? Ok(result)
                : StatusCode(500, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual sync from remote");
            return StatusCode(
                500,
                new SyncResultDto
                {
                    Errors = [$"Sync failed: {ex.Message}"],
                    SyncCompletedAt = DateTime.UtcNow,
                }
            );
        }
    }

    /// <summary>
    /// Manually trigger sync to remote API (push operation)
    /// </summary>
    [HttpPost("push")]
    public async Task<ActionResult<SyncResultDto>> SyncToRemote(
        CancellationToken cancellationToken
    )
    {
        try
        {
            _logger.LogInformation("Manual sync to remote triggered via API");

            var result = await _syncService.SyncToRemoteAsync(cancellationToken);

            return result.Success
                ? Ok(result)
                : StatusCode(500, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual sync to remote");
            return StatusCode(
                500,
                new SyncResultDto
                {
                    Errors = [$"Sync failed: {ex.Message}"],
                    SyncCompletedAt = DateTime.UtcNow,
                }
            );
        }
    }

    /// <summary>
    /// Manually trigger full bidirectional sync (pull then push)
    /// </summary>
    [HttpPost("full")]
    public async Task<ActionResult<SyncResultDto>> FullSync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Manual full bidirectional sync triggered via API");

            var result = await _syncService.FullSyncAsync(cancellationToken);

            return result.Success
                ? Ok(result)
                : StatusCode(500, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual full sync");
            return StatusCode(
                500,
                new SyncResultDto
                {
                    Errors = [$"Full sync failed: {ex.Message}"],
                    SyncCompletedAt = DateTime.UtcNow,
                }
            );
        }
    }

    /// <summary>
    /// Get sync status information
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetSyncStatus()
    {
        return Ok(
            new
            {
                Message = "Sync status endpoint - to be implemented with sync tracking",
                Timestamp = DateTime.UtcNow,
            }
        );
    }
}
