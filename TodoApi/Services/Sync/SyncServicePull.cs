using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Dtos.External;
using TodoApi.Dtos.Sync;
using TodoApi.Models;

namespace TodoApi.Services.Sync;

public class SyncServicePull : ISyncService
{
    private readonly IExternalTodoApiClient _externalApiClient;
    private readonly TodoContext _context;
    private readonly ILogger<SyncServicePull> _logger;

    public SyncServicePull(
        IExternalTodoApiClient externalApiClient,
        TodoContext context,
        ILogger<SyncServicePull> logger
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

    public async Task<SyncResultDto> SyncToRemoteAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = new SyncResultDto();

        try
        {
            _logger.LogInformation("Starting sync to remote API");

            // Load all local lists with items
            var localLists = await _context.TodoList
                .Include(tl => tl.TodoItems)
                .ToListAsync(cancellationToken);

            // Phase 1: Create new TodoLists (RemoteId == null)
            var newLists = localLists.Where(l => l.RemoteId == null).ToList();
            foreach (var localList in newLists)
            {
                try
                {
                    await CreateTodoListOnRemoteAsync(localList, result, cancellationToken);
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error creating TodoList '{localList.Name}' on remote: {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    result.Errors.Add(errorMsg);
                }
            }

            // Phase 2: Update modified TodoLists
            var modifiedLists = localLists
                .Where(l =>
                    l is { RemoteId: not null, LastSyncedAt: not null }
                    && l.UpdatedAt > l.LastSyncedAt.Value
                )
                .ToList();

            foreach (var localList in modifiedLists)
            {
                try
                {
                    await UpdateTodoListOnRemoteAsync(localList, result, cancellationToken);
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error updating TodoList '{localList.Name}' on remote: {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    result.Errors.Add(errorMsg);
                }
            }

            // Phase 3: Update modified TodoItems
            var allItems = localLists.SelectMany(l => l.TodoItems).ToList();
            var modifiedItems = allItems
                .Where(i =>
                    i is { RemoteId: not null, LastSyncedAt: not null }
                    && i.UpdatedAt > i.LastSyncedAt.Value
                )
                .ToList();

            foreach (var localItem in modifiedItems)
            {
                try
                {
                    var parentList = localLists.First(l => l.Id == localItem.TodoListId);
                    await UpdateTodoItemOnRemoteAsync(
                        localItem,
                        parentList,
                        result,
                        cancellationToken
                    );
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error updating TodoItem '{localItem.Description}' on remote: {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    result.Errors.Add(errorMsg);
                }
            }

            _logger.LogInformation(
                "Sync to remote completed. "
                    + "Lists: {ListsCreated} created, {ListsUpdated} updated. "
                    + "Items: {ItemsCreated} created, {ItemsUpdated} updated. "
                    + "Errors: {ErrorCount}",
                result.ListsCreated,
                result.ListsUpdated,
                result.ItemsCreated,
                result.ItemsUpdated,
                result.Errors.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during sync to remote");
            result.Errors.Add($"Fatal sync error: {ex.Message}");
        }

        result.SyncCompletedAt = DateTime.UtcNow;

        return result;
    }

    public async Task<SyncResultDto> FullSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full bidirectional sync");

        var combinedResult = new SyncResultDto();

        try
        {
            // Step 1: Pull from remote
            _logger.LogInformation("Step 1: Pulling changes from remote");
            var pullResult = await SyncFromRemoteAsync(cancellationToken);

            // Step 2: Push to remote
            _logger.LogInformation("Step 2: Pushing changes to remote");
            var pushResult = await SyncToRemoteAsync(cancellationToken);

            // Step 3: Combine results
            combinedResult.ListsCreated = pullResult.ListsCreated + pushResult.ListsCreated;
            combinedResult.ListsUpdated = pullResult.ListsUpdated + pushResult.ListsUpdated;
            combinedResult.ListsSkipped = pullResult.ListsSkipped;
            combinedResult.ItemsCreated = pullResult.ItemsCreated + pushResult.ItemsCreated;
            combinedResult.ItemsUpdated = pullResult.ItemsUpdated + pushResult.ItemsUpdated;
            combinedResult.ItemsSkipped = pullResult.ItemsSkipped;
            combinedResult.Errors.AddRange(pullResult.Errors);
            combinedResult.Errors.AddRange(pushResult.Errors);
            combinedResult.SyncCompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Full sync completed. " +
                                   "Pull: {PullListsCreated}+{PullItemsCreated}, " +
                                   "Push: {PushListsCreated}+{PushItemsCreated}. " +
                                   "Errors: {ErrorCount}",
                pullResult.ListsCreated,
                pullResult.ItemsCreated,
                pushResult.ListsCreated,
                pushResult.ItemsCreated,
                combinedResult.Errors.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during full sync");
            combinedResult.Errors.Add($"Fatal full sync error: {ex.Message}");
            combinedResult.SyncCompletedAt = DateTime.UtcNow;
        }

        return combinedResult;
    }

    private async Task CreateTodoListOnRemoteAsync(
        TodoList localList,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation("Creating TodoList on remote: {Name}", localList.Name);

        var request = new CreateTodoListRequestDto
        {
            SourceId = localList.SourceId ?? localList.Id.ToString(),
            Name = localList.Name,
            Items = localList
                .TodoItems.Select(i => new CreateTodoItemRequestDto
                {
                    SourceId = i.SourceId ?? i.Id.ToString(),
                    Description = i.Description,
                    Completed = i.Completed,
                })
                .ToList(),
        };

        var createdList = await _externalApiClient.CreateTodoListAsync(request, cancellationToken);

        // Update local entity with RemoteId
        localList.RemoteId = createdList.Id;
        localList.LastSyncedAt = DateTime.UtcNow;

        // Update items with their RemoteIds
        for (var i = 0; i < localList.TodoItems.Count; i++)
        {
            if (i < createdList.Items.Count)
            {
                localList.TodoItems[i].RemoteId = createdList.Items[i].Id;
                localList.TodoItems[i].LastSyncedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created TodoList on remote: {Name} (RemoteId: {RemoteId}, Items: {ItemCount})",
            localList.Name,
            localList.RemoteId,
            localList.TodoItems.Count
        );

        result.ListsCreated++;
        result.ItemsCreated += localList.TodoItems.Count;
    }

    private async Task UpdateTodoListOnRemoteAsync(
        TodoList localList,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(localList.RemoteId))
        {
            _logger.LogWarning(
                "Cannot update TodoList without RemoteId: {Name} (Id: {Id})",
                localList.Name,
                localList.Id
            );
            return;
        }

        _logger.LogInformation(
            "Updating TodoList on remote: {Name} (RemoteId: {RemoteId})",
            localList.Name,
            localList.RemoteId
        );

        var request = new UpdateTodoListRequestDto { Name = localList.Name };

        await _externalApiClient.UpdateTodoListAsync(
            localList.RemoteId,
            request,
            cancellationToken
        );

        localList.LastSyncedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated TodoList on remote: {RemoteId}", localList.RemoteId);
        result.ListsUpdated++;
    }

    private async Task UpdateTodoItemOnRemoteAsync(
        TodoItem localItem,
        TodoList parentList,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(localItem.RemoteId) || string.IsNullOrEmpty(parentList.RemoteId))
        {
            _logger.LogWarning(
                "Cannot update TodoItem without RemoteId: {Description} (Id: {Id})",
                localItem.Description,
                localItem.Id
            );
            return;
        }

        _logger.LogInformation(
            "Updating TodoItem on remote: {Description} (RemoteId: {RemoteId})",
            localItem.Description,
            localItem.RemoteId
        );

        var request = new UpdateTodoItemRequestDto
        {
            Description = localItem.Description,
            Completed = localItem.Completed,
        };

        await _externalApiClient.UpdateTodoItemAsync(
            parentList.RemoteId,
            localItem.RemoteId,
            request,
            cancellationToken
        );

        localItem.LastSyncedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated TodoItem on remote: {RemoteId}", localItem.RemoteId);
        result.ItemsUpdated++;
    }
}
