using TodoApi.Dtos.External;

namespace TodoApi.Services.Sync;

public interface IExternalTodoApiClient
{
    Task<List<ExternalTodoListDto>> GetAllTodoListsAsync(CancellationToken cancellationToken = default);
}
