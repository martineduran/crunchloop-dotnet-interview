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
    public async Task<ActionResult<SyncResultDto>> SyncFromRemote(
        CancellationToken cancellationToken
    )
    {
        try
        {
            _logger.LogInformation("Manual sync from remote triggered via API");

            var result = await _syncService.SyncFromRemoteAsync(cancellationToken);

            if (result.Success)
            {
                return Ok(result);
            }
            else
            {
                return StatusCode(500, result);
            }
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
    /// Get sync status information
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetSyncStatus()
    {
        // This is a placeholder for future implementation
        // Could track last sync time, next scheduled sync, etc.
        return Ok(
            new
            {
                Message = "Sync status endpoint - to be implemented with sync tracking",
                Timestamp = DateTime.UtcNow,
            }
        );
    }
}
