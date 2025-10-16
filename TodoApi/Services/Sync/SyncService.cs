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

            // Detect and delete local entities that no longer exist remotely
            await DetectAndDeleteRemovedEntitiesAsync(remoteLists, localLists, result, cancellationToken);

            _logger.LogInformation("Sync from remote completed. " +
                                   "Lists: {ListsCreated} created, {ListsUpdated} updated, {ListsSkipped} skipped, {ListsDeleted} deleted. " +
                                   "Items: {ItemsCreated} created, {ItemsUpdated} updated, {ItemsSkipped} skipped, {ItemsDeleted} deleted. " +
                                   "Errors: {ErrorCount}",
                result.ListsCreated,
                result.ListsUpdated,
                result.ListsSkipped,
                result.ListsDeleted,
                result.ItemsCreated,
                result.ItemsUpdated,
                result.ItemsSkipped,
                result.ItemsDeleted,
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

            // Phase 4: Delete entities that were removed locally
            await PushDeletionsAsync(result, cancellationToken);

            _logger.LogInformation(
                "Sync to remote completed. "
                    + "Lists: {ListsCreated} created, {ListsUpdated} updated, {ListsDeleted} deleted. "
                    + "Items: {ItemsCreated} created, {ItemsUpdated} updated, {ItemsDeleted} deleted. "
                    + "Errors: {ErrorCount}",
                result.ListsCreated,
                result.ListsUpdated,
                result.ListsDeleted,
                result.ItemsCreated,
                result.ItemsUpdated,
                result.ItemsDeleted,
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
            combinedResult.ListsDeleted = pullResult.ListsDeleted + pushResult.ListsDeleted;
            combinedResult.ItemsCreated = pullResult.ItemsCreated + pushResult.ItemsCreated;
            combinedResult.ItemsUpdated = pullResult.ItemsUpdated + pushResult.ItemsUpdated;
            combinedResult.ItemsSkipped = pullResult.ItemsSkipped;
            combinedResult.ItemsDeleted = pullResult.ItemsDeleted + pushResult.ItemsDeleted;
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
    
    private async Task PushDeletionsAsync(
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        // Get all pending deletions
        var pendingDeletions = await _context.DeletedEntities.ToListAsync(cancellationToken);

        if (!pendingDeletions.Any())
        {
            return;
        }

        _logger.LogInformation("Processing {Count} pending deletions", pendingDeletions.Count);

        // Delete TodoItems first
        var deletedItems = pendingDeletions
            .Where(d => d.EntityType == "TodoItem")
            .ToList();
        foreach (var deleted in deletedItems)
        {
            try
            {
                if (string.IsNullOrEmpty(deleted.ParentRemoteId))
                {
                    _logger.LogWarning(
                        "Skipping TodoItem deletion - missing ParentRemoteId: {RemoteId}",
                        deleted.RemoteId
                    );
                    continue;
                }

                _logger.LogInformation(
                    "Deleting TodoItem on remote: {RemoteId} (Parent: {ParentRemoteId})",
                    deleted.RemoteId,
                    deleted.ParentRemoteId
                );

                await _externalApiClient.DeleteTodoItemAsync(
                    deleted.ParentRemoteId,
                    deleted.RemoteId,
                    cancellationToken
                );

                // Clean up tombstone after successful deletion
                _context.DeletedEntities.Remove(deleted);
                await _context.SaveChangesAsync(cancellationToken);

                result.ItemsDeleted++;
                _logger.LogInformation("Deleted TodoItem on remote: {RemoteId}", deleted.RemoteId);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already deleted remotely, just clean up tombstone
                _logger.LogInformation(
                    "TodoItem already deleted remotely, cleaning up tombstone: {RemoteId}",
                    deleted.RemoteId
                );
                _context.DeletedEntities.Remove(deleted);
                await _context.SaveChangesAsync(cancellationToken);
                result.ItemsDeleted++;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error deleting TodoItem {deleted.RemoteId} on remote: {ex.Message}";
                _logger.LogError(ex, errorMsg);
                result.Errors.Add(errorMsg);
            }
        }

        // Delete TodoLists after items
        var deletedLists = pendingDeletions.Where(d => d.EntityType == "TodoList").ToList();
        foreach (var deleted in deletedLists)
        {
            try
            {
                _logger.LogInformation(
                    "Deleting TodoList on remote: {RemoteId}",
                    deleted.RemoteId
                );

                await _externalApiClient.DeleteTodoListAsync(
                    deleted.RemoteId,
                    cancellationToken
                );

                // Clean up tombstone after successful deletion
                _context.DeletedEntities.Remove(deleted);
                await _context.SaveChangesAsync(cancellationToken);

                result.ListsDeleted++;
                _logger.LogInformation("Deleted TodoList on remote: {RemoteId}", deleted.RemoteId);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already deleted remotely, just clean up tombstone
                _logger.LogInformation(
                    "TodoList already deleted remotely, cleaning up tombstone: {RemoteId}",
                    deleted.RemoteId
                );
                _context.DeletedEntities.Remove(deleted);
                await _context.SaveChangesAsync(cancellationToken);
                result.ListsDeleted++;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error deleting TodoList {deleted.RemoteId} on remote: {ex.Message}";
                _logger.LogError(ex, errorMsg);
                result.Errors.Add(errorMsg);
            }
        }

        _logger.LogInformation(
            "Completed push deletions. Lists: {ListsDeleted}, Items: {ItemsDeleted}",
            result.ListsDeleted,
            result.ItemsDeleted
        );
    }

    private async Task DetectAndDeleteRemovedEntitiesAsync(
        List<ExternalTodoListDto> remoteLists,
        List<TodoList> localLists,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        // Build set of remote IDs for fast lookup
        var remoteListIds = remoteLists
            .Select(rl => rl.Id)
            .Where(id => id != null)
            .ToHashSet();
        var remoteItemIdsByList = remoteLists
            .Where(rl => rl.Id != null)
            .ToDictionary(
                rl => rl.Id!,
                rl => rl.Items
                    .Select(ri => ri.Id)
                    .Where(id => id != null)
                    .ToHashSet()
            );
        
        // Detect and delete TodoLists that were deleted remotely
        await DetectAndDeleteRemovedTodoListsAsync(localLists, remoteListIds, result, cancellationToken);
        
        // Detect and delete TodoItems that were deleted remotely
        await DetectAndDeleteRemovedTodoItemsAsync(
            localLists,
            remoteListIds,
            remoteItemIdsByList,
            result,
            cancellationToken
        );
    }
    
    private async Task DetectAndDeleteRemovedTodoListsAsync(
        List<TodoList> localLists,
        HashSet<string?> remoteListIds,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        var deletedLists = localLists
            .Where(ll => !string.IsNullOrEmpty(ll.RemoteId) && !remoteListIds.Contains(ll.RemoteId))
            .ToList();

        foreach (var deletedList in deletedLists)
        {
            try
            {
                _logger.LogInformation(
                    "Deleting local TodoList (no longer exists remotely): {Name} (Id: {Id}, RemoteId: {RemoteId})",
                    deletedList.Name,
                    deletedList.Id,
                    deletedList.RemoteId
                );

                var itemCount = deletedList.TodoItems.Count;

                // Manually delete items first
                foreach (var item in deletedList.TodoItems.ToList())
                {
                    _context.TodoItems.Remove(item);
                }

                _context.TodoList.Remove(deletedList);
                await _context.SaveChangesAsync(cancellationToken);

                result.ListsDeleted++;
                result.ItemsDeleted += itemCount;

                _logger.LogInformation(
                    "Deleted TodoList and {ItemCount} items: {Name} (RemoteId: {RemoteId})",
                    itemCount,
                    deletedList.Name,
                    deletedList.RemoteId
                );
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error deleting TodoList '{deletedList.Name}' (RemoteId: {deletedList.RemoteId}): {ex.Message}";
                _logger.LogError(ex, errorMsg);
                result.Errors.Add(errorMsg);
            }
        }
    }
    
    private async Task DetectAndDeleteRemovedTodoItemsAsync(
        List<TodoList> localLists,
        HashSet<string?> remoteListIds,
        Dictionary<string, HashSet<string?>> remoteItemIdsByList,
        SyncResultDto result,
        CancellationToken cancellationToken
    )
    {
        var listsStillInRemote = localLists
            .Where(ll => !string.IsNullOrEmpty(ll.RemoteId) && remoteListIds.Contains(ll.RemoteId))
            .ToList();

        foreach (var localList in listsStillInRemote)
        {
            if (!remoteItemIdsByList.TryGetValue(localList.RemoteId!, out var remoteItemIds))
            {
                continue;
            }

            var deletedItems = localList.TodoItems
                .Where(li => !string.IsNullOrEmpty(li.RemoteId) && !remoteItemIds.Contains(li.RemoteId))
                .ToList();

            foreach (var deletedItem in deletedItems)
            {
                try
                {
                    _logger.LogInformation(
                        "Deleting local TodoItem (no longer exists remotely): {Description} (Id: {Id}, RemoteId: {RemoteId})",
                        deletedItem.Description,
                        deletedItem.Id,
                        deletedItem.RemoteId
                    );

                    _context.TodoItems.Remove(deletedItem);
                    await _context.SaveChangesAsync(cancellationToken);

                    result.ItemsDeleted++;

                    _logger.LogInformation(
                        "Deleted TodoItem: {Description} (RemoteId: {RemoteId})",
                        deletedItem.Description,
                        deletedItem.RemoteId
                    );
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Error deleting TodoItem '{deletedItem.Description}' (RemoteId: {deletedItem.RemoteId}): {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    result.Errors.Add(errorMsg);
                }
            }
        }        
    }
}
