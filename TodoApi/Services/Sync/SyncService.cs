using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Dtos.External;
using TodoApi.Dtos.Sync;
using TodoApi.Models;

namespace TodoApi.Services.Sync;

public class SyncService : ISyncService
{
    private readonly IExternalTodoApiClient _externalApiClient;
    private readonly TodoContext _context;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        IExternalTodoApiClient externalApiClient,
        TodoContext context,
        ILogger<SyncService> logger
    )
    {
        _externalApiClient = externalApiClient;
        _context = context;
        _logger = logger;
    }

    public async Task<SyncResultDto> SyncFromRemoteAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = new SyncResultDto();

        try
        {
            _logger.LogInformation("Starting sync from remote API");

            // Fetch all remote lists with items
            var remoteLists = await _externalApiClient.GetAllTodoListsAsync(cancellationToken);

            // Load all local lists with items for matching
            var localLists = await _context.TodoList
                .Include(tl => tl.TodoItems)
                .ToListAsync(cancellationToken);

            foreach (var remoteList in remoteLists)
            {
                try
                {
                    await SyncTodoListAsync(remoteList, localLists, result, cancellationToken);
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error syncing TodoList '{remoteList.Name}' (RemoteId: {remoteList.Id}): {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    result.Errors.Add(errorMsg);
                }
            }
            
            _logger.LogInformation("Sync from remote completed. " +
                                   "Lists: {ListsCreated} created, {ListsUpdated} updated, {ListsSkipped} skipped. " +
                                   "Items: {ItemsCreated} created, {ItemsUpdated} updated, {ItemsSkipped} skipped. " +
                                   "Errors: {ErrorCount}",
                result.ListsCreated,
                result.ListsUpdated,
                result.ListsSkipped,
                result.ItemsCreated,
                result.ItemsUpdated,
                result.ItemsSkipped,
                result.Errors.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during sync from remote");
            result.Errors.Add($"Fatal sync error: {ex.Message}");
        }
        
        result.SyncCompletedAt = DateTime.UtcNow;
        
        return result;
    }

    private async Task SyncTodoListAsync(
        ExternalTodoListDto remoteList,
        List<TodoList> localLists,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        // Match by RemoteId first, then by SourceId, then by Name
        var localList = localLists.FirstOrDefault(l => l.RemoteId == remoteList.Id);

        if (localList == null && !string.IsNullOrEmpty(remoteList.SourceId))
        {
            localList = localLists.FirstOrDefault(l => l.SourceId == remoteList.SourceId);
        }

        if (localList == null && !string.IsNullOrEmpty(remoteList.Name))
        {
            localList = localLists.FirstOrDefault(l =>
                l.Name.Equals(remoteList.Name, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (localList == null)
        {
            // Create new local TodoList
            localList = new TodoList
            {
                Name = remoteList.Name ?? "Untitled",
                RemoteId = remoteList.Id,
                SourceId = remoteList.SourceId,
                CreatedAt = remoteList.CreatedAt,
                UpdatedAt = remoteList.UpdatedAt,
                LastSyncedAt = DateTime.UtcNow,
                TodoItems = [],
            };

            _context.TodoList.Add(localList);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created new TodoList: {Name} (RemoteId: {RemoteId})",
                localList.Name,
                localList.RemoteId
            );
            result.ListsCreated++;

            // Add to local cache for item matching
            localLists.Add(localList);
        }
        else
        {
            // Check if remote is newer (last-write-wins)
            if (remoteList.UpdatedAt > localList.UpdatedAt)
            {
                localList.Name = remoteList.Name ?? localList.Name;
                localList.RemoteId = remoteList.Id;
                localList.SourceId ??= remoteList.SourceId;
                localList.UpdatedAt = remoteList.UpdatedAt;
                localList.LastSyncedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Updated TodoList: {Name} (Id: {Id}, RemoteId: {RemoteId})",
                    localList.Name,
                    localList.Id,
                    localList.RemoteId
                );
                result.ListsUpdated++;
            }
            else
            {
                // Update sync metadata even if we didn't update the list
                localList.RemoteId = remoteList.Id;
                localList.SourceId ??= remoteList.SourceId;
                localList.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogDebug(
                    "Skipped updating TodoList (local is newer): {Name} (Id: {Id})",
                    localList.Name,
                    localList.Id
                );
                result.ListsSkipped++;
            }
        }

        // Sync TodoItems
        foreach (var remoteItem in remoteList.Items)
        {
            await SyncTodoItemAsync(remoteItem, localList, result, cancellationToken);
        }
    }

    private async Task SyncTodoItemAsync(
        ExternalTodoItemDto remoteItem,
        TodoList localList,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        // Match by RemoteId first, then by SourceId, then by Description
        var localItem = localList.TodoItems.FirstOrDefault(i => i.RemoteId == remoteItem.Id);

        if (localItem == null && !string.IsNullOrEmpty(remoteItem.SourceId))
        {
            localItem = localList.TodoItems.FirstOrDefault(i => i.SourceId == remoteItem.SourceId);
        }

        if (localItem == null && !string.IsNullOrEmpty(remoteItem.Description))
        {
            localItem = localList.TodoItems.FirstOrDefault(i =>
                i.Description.Equals(remoteItem.Description, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (localItem == null)
        {
            // Create new local TodoItem
            localItem = new TodoItem
            {
                Description = remoteItem.Description ?? "No description",
                Completed = remoteItem.Completed,
                TodoListId = localList.Id,
                RemoteId = remoteItem.Id,
                SourceId = remoteItem.SourceId,
                CreatedAt = remoteItem.CreatedAt,
                UpdatedAt = remoteItem.UpdatedAt,
                LastSyncedAt = DateTime.UtcNow,
            };

            localList.TodoItems.Add(localItem);
            _context.TodoItems.Add(localItem);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created new TodoItem: {Description} in list {ListName} (RemoteId: {RemoteId})",
                localItem.Description,
                localList.Name,
                localItem.RemoteId
            );
            result.ItemsCreated++;
        }
        else
        {
            // Check if remote is newer (last-write-wins)
            if (remoteItem.UpdatedAt > localItem.UpdatedAt)
            {
                localItem.Description = remoteItem.Description ?? localItem.Description;
                localItem.Completed = remoteItem.Completed;
                localItem.RemoteId = remoteItem.Id;
                localItem.SourceId ??= remoteItem.SourceId;
                localItem.UpdatedAt = remoteItem.UpdatedAt;
                localItem.LastSyncedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Updated TodoItem: {Description} (Id: {Id}, RemoteId: {RemoteId})",
                    localItem.Description,
                    localItem.Id,
                    localItem.RemoteId
                );
                result.ItemsUpdated++;
            }
            else
            {
                // Update sync metadata even if we didn't update the item
                localItem.RemoteId = remoteItem.Id;
                localItem.SourceId ??= remoteItem.SourceId;
                localItem.LastSyncedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogDebug(
                    "Skipped updating TodoItem (local is newer): {Description} (Id: {Id})",
                    localItem.Description,
                    localItem.Id
                );
                result.ItemsSkipped++;
            }
        }
    }
}
