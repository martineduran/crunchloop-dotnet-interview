using TodoApi.Dtos.Sync;

namespace TodoApi.Services.Sync;

public interface ISyncService
{
    Task<SyncResultDto> SyncFromRemoteAsync(CancellationToken cancellationToken = default);
    Task<SyncResultDto> SyncToRemoteAsync(CancellationToken cancellationToken = default);
    Task<SyncResultDto> FullSyncAsync(CancellationToken cancellationToken = default);
}
