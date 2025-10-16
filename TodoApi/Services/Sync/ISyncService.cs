using TodoApi.Dtos.Sync;

namespace TodoApi.Services.Sync;

public interface ISyncService
{
    Task<SyncResultDto> SyncFromRemoteAsync(CancellationToken cancellationToken = default);
}
