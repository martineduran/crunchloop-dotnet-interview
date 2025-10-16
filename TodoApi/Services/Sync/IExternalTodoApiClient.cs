using TodoApi.Dtos.External;

namespace TodoApi.Services.Sync;

public interface IExternalTodoApiClient
{
    Task<List<ExternalTodoListDto>> GetAllTodoListsAsync(CancellationToken cancellationToken = default);
    Task<ExternalTodoListDto> CreateTodoListAsync(CreateTodoListRequestDto request, 
        CancellationToken cancellationToken = default);
    Task<ExternalTodoListDto> UpdateTodoListAsync(
        string todolistId,
        UpdateTodoListRequestDto request,
        CancellationToken cancellationToken = default
    );
    Task DeleteTodoListAsync(string todolistId, CancellationToken cancellationToken = default);
    Task<ExternalTodoItemDto> UpdateTodoItemAsync(
        string todolistId,
        string todoitemId,
        UpdateTodoItemRequestDto request,
        CancellationToken cancellationToken = default
    );
    Task DeleteTodoItemAsync(string todolistId, string todoitemId, CancellationToken cancellationToken = default);
}
