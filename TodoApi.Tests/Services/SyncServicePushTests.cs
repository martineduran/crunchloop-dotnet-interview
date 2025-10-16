using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TodoApi.Data;
using TodoApi.Dtos.External;
using TodoApi.Models;
using TodoApi.Services.Sync;

namespace TodoApi.Tests.Services;

public class SyncServicePushTests : IDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<ILogger<SyncService>> _mockLogger;
    private readonly SyncService _syncService;

    public SyncServicePushTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TodoContext(options);
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockLogger = new Mock<ILogger<SyncService>>();

        _syncService = new SyncService(_mockExternalClient.Object, _context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task SyncToRemoteAsync_CreatesNewTodoList_WhenRemoteIdIsNull()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "Local List",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteResponse = new ExternalTodoListDto
        {
            Id = "remote-1",
            Name = "Local List",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Items = [],
        };

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateTodoListRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteResponse);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(1, result.ListsCreated);
        Assert.Equal(0, result.ListsUpdated);
        Assert.Empty(result.Errors);

        var updatedList = await _context.TodoList.FirstAsync();
        Assert.Equal("remote-1", updatedList.RemoteId);
        Assert.NotNull(updatedList.LastSyncedAt);
    }

    [Fact]
    public async Task SyncToRemoteAsync_CreatesListWithItems_WhenRemoteIdIsNull()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "List with Items",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TodoItems =
            [
                new TodoItem
                {
                    Description = "Item 1",
                    Completed = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
                new TodoItem
                {
                    Description = "Item 2",
                    Completed = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
            ],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteResponse = new ExternalTodoListDto
        {
            Id = "remote-list-1",
            Name = "List with Items",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Items =
            [
                new ExternalTodoItemDto
                {
                    Id = "remote-item-1",
                    Description = "Item 1",
                    Completed = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
                new ExternalTodoItemDto
                {
                    Id = "remote-item-2",
                    Description = "Item 2",
                    Completed = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
            ],
        };

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateTodoListRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteResponse);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(1, result.ListsCreated);
        Assert.Equal(2, result.ItemsCreated);
        Assert.Empty(result.Errors);

        var updatedList = await _context.TodoList.Include(l => l.TodoItems).FirstAsync();
        Assert.Equal("remote-list-1", updatedList.RemoteId);
        Assert.Equal("remote-item-1", updatedList.TodoItems[0].RemoteId);
        Assert.Equal("remote-item-2", updatedList.TodoItems[1].RemoteId);
    }

    [Fact]
    public async Task SyncToRemoteAsync_UpdatesModifiedTodoList_WhenHasRemoteId()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var newTime = DateTime.UtcNow.AddMinutes(-5);

        var localList = new TodoList
        {
            Name = "Modified List",
            RemoteId = "remote-1",
            CreatedAt = oldTime,
            UpdatedAt = newTime,
            LastSyncedAt = oldTime,
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteResponse = new ExternalTodoListDto
        {
            Id = "remote-1",
            Name = "Modified List",
            CreatedAt = oldTime,
            UpdatedAt = newTime,
            Items = [],
        };

        _mockExternalClient
            .Setup(x =>
                x.UpdateTodoListAsync(
                    "remote-1",
                    It.IsAny<UpdateTodoListRequestDto>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(remoteResponse);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(1, result.ListsUpdated);
        Assert.Empty(result.Errors);

        _mockExternalClient.Verify(
            x =>
                x.UpdateTodoListAsync(
                    "remote-1",
                    It.IsAny<UpdateTodoListRequestDto>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncToRemoteAsync_UpdatesModifiedTodoItem_WhenHasRemoteId()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var newTime = DateTime.UtcNow.AddMinutes(-5);

        var localList = new TodoList
        {
            Name = "List",
            RemoteId = "remote-list-1",
            CreatedAt = oldTime,
            UpdatedAt = oldTime,
            LastSyncedAt = oldTime,
            TodoItems =
            [
                new TodoItem
                {
                    Description = "Modified Item",
                    Completed = true,
                    RemoteId = "remote-item-1",
                    CreatedAt = oldTime,
                    UpdatedAt = newTime,
                    LastSyncedAt = oldTime,
                },
            ],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteResponse = new ExternalTodoItemDto
        {
            Id = "remote-item-1",
            Description = "Modified Item",
            Completed = true,
            CreatedAt = oldTime,
            UpdatedAt = newTime,
        };

        _mockExternalClient
            .Setup(x =>
                x.UpdateTodoItemAsync(
                    "remote-list-1",
                    "remote-item-1",
                    It.IsAny<UpdateTodoItemRequestDto>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(remoteResponse);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(0, result.ItemsCreated);
        Assert.Equal(1, result.ItemsUpdated);
        Assert.Empty(result.Errors);

        _mockExternalClient.Verify(
            x =>
                x.UpdateTodoItemAsync(
                    "remote-list-1",
                    "remote-item-1",
                    It.IsAny<UpdateTodoItemRequestDto>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncToRemoteAsync_SkipsUnmodifiedEntities()
    {
        // Arrange
        var syncTime = DateTime.UtcNow;

        var localList = new TodoList
        {
            Name = "Unmodified List",
            RemoteId = "remote-1",
            CreatedAt = syncTime,
            UpdatedAt = syncTime,
            LastSyncedAt = syncTime,
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(0, result.ListsUpdated);
        Assert.Empty(result.Errors);

        _mockExternalClient.Verify(
            x => x.CreateTodoListAsync(It.IsAny<CreateTodoListRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        _mockExternalClient.Verify(
            x =>
                x.UpdateTodoListAsync(
                    It.IsAny<string>(),
                    It.IsAny<UpdateTodoListRequestDto>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task FullSyncAsync_ExecutesPullThenPush()
    {
        // Arrange - Set up pull scenario
        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                Name = "Remote List",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Items = [],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Arrange - Set up push scenario
        var localList = new TodoList
        {
            Name = "Local List",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var createResponse = new ExternalTodoListDto
        {
            Id = "remote-2",
            Name = "Local List",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Items = [],
        };

        _mockExternalClient
            .Setup(x => x.CreateTodoListAsync(It.IsAny<CreateTodoListRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createResponse);

        // Act
        var result = await _syncService.FullSyncAsync();

        // Assert
        Assert.Equal(2, result.ListsCreated); // 1 from pull, 1 from push
        Assert.Empty(result.Errors);

        // Verify pull happened
        _mockExternalClient.Verify(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Verify push happened
        _mockExternalClient.Verify(
            x => x.CreateTodoListAsync(It.IsAny<CreateTodoListRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncToRemoteAsync_DeletesTodoListOnRemote_WhenDeletedLocally()
    {
        // Arrange - Create a tombstone for a deleted list
        var tombstone = new DeletedEntity
        {
            RemoteId = "remote-list-1",
            EntityType = "TodoList",
            DeletedAt = DateTime.UtcNow
        };

        _context.DeletedEntities.Add(tombstone);
        await _context.SaveChangesAsync();

        _mockExternalClient
            .Setup(x => x.DeleteTodoListAsync("remote-list-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(1, result.ListsDeleted);
        Assert.Equal(0, result.ItemsDeleted);
        Assert.Empty(result.Errors);

        // Verify tombstone was cleaned up
        var remainingTombstones = await _context.DeletedEntities.ToListAsync();
        Assert.Empty(remainingTombstones);

        // Verify DELETE was called
        _mockExternalClient.Verify(
            x => x.DeleteTodoListAsync("remote-list-1", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncToRemoteAsync_DeletesTodoItemOnRemote_WhenDeletedLocally()
    {
        // Arrange - Create a tombstone for a deleted item
        var tombstone = new DeletedEntity
        {
            RemoteId = "remote-item-1",
            EntityType = "TodoItem",
            DeletedAt = DateTime.UtcNow,
            ParentRemoteId = "remote-list-1"
        };

        _context.DeletedEntities.Add(tombstone);
        await _context.SaveChangesAsync();

        _mockExternalClient
            .Setup(x => x.DeleteTodoItemAsync("remote-list-1", "remote-item-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsDeleted);
        Assert.Equal(1, result.ItemsDeleted);
        Assert.Empty(result.Errors);

        // Verify tombstone was cleaned up
        var remainingTombstones = await _context.DeletedEntities.ToListAsync();
        Assert.Empty(remainingTombstones);

        // Verify DELETE was called
        _mockExternalClient.Verify(
            x => x.DeleteTodoItemAsync("remote-list-1", "remote-item-1", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task SyncToRemoteAsync_HandlesAlreadyDeletedEntity_WhenRemoteReturns404()
    {
        // Arrange - Create tombstone for entity already deleted remotely
        var tombstone = new DeletedEntity
        {
            RemoteId = "remote-list-1",
            EntityType = "TodoList",
            DeletedAt = DateTime.UtcNow
        };

        _context.DeletedEntities.Add(tombstone);
        await _context.SaveChangesAsync();

        // Mock 404 response
        _mockExternalClient
            .Setup(x => x.DeleteTodoListAsync("remote-list-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Not Found", null, System.Net.HttpStatusCode.NotFound));

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert - Should handle 404 gracefully
        Assert.Equal(1, result.ListsDeleted); // Count as success
        Assert.Empty(result.Errors); // No errors

        // Verify tombstone was cleaned up despite 404
        var remainingTombstones = await _context.DeletedEntities.ToListAsync();
        Assert.Empty(remainingTombstones);
    }

    [Fact]
    public async Task SyncToRemoteAsync_CleansTombstones_AfterSuccessfulDeletion()
    {
        // Arrange - Create multiple tombstones
        var listTombstone = new DeletedEntity
        {
            RemoteId = "remote-list-1",
            EntityType = "TodoList",
            DeletedAt = DateTime.UtcNow
        };

        var itemTombstone = new DeletedEntity
        {
            RemoteId = "remote-item-1",
            EntityType = "TodoItem",
            DeletedAt = DateTime.UtcNow,
            ParentRemoteId = "remote-list-2"
        };

        _context.DeletedEntities.AddRange(listTombstone, itemTombstone);
        await _context.SaveChangesAsync();

        _mockExternalClient
            .Setup(x => x.DeleteTodoListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockExternalClient
            .Setup(x => x.DeleteTodoItemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _syncService.SyncToRemoteAsync();

        // Assert
        Assert.Equal(1, result.ListsDeleted);
        Assert.Equal(1, result.ItemsDeleted);
        Assert.Empty(result.Errors);

        // Verify all tombstones were cleaned up
        var remainingTombstones = await _context.DeletedEntities.ToListAsync();
        Assert.Empty(remainingTombstones);
    }

    [Fact]
    public async Task SyncToRemoteAsync_DoesNotCreateTombstone_WhenEntityNeverSynced()
    {
        // Arrange - Create a local-only list (no RemoteId)
        var localOnlyList = new TodoList
        {
            Name = "Local Only List",
            RemoteId = null, // Never synced
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TodoItems = []
        };

        _context.TodoList.Add(localOnlyList);
        await _context.SaveChangesAsync();

        // Act - Simulate deletion through controller logic
        // (Controller should not create tombstone for entity without RemoteId)
        var listToDelete = await _context.TodoList.Include(l => l.TodoItems).FirstAsync();

        // Don't create tombstone since RemoteId is null
        if (!string.IsNullOrEmpty(listToDelete.RemoteId))
        {
            var tombstone = new DeletedEntity
            {
                RemoteId = listToDelete.RemoteId,
                EntityType = "TodoList",
                DeletedAt = DateTime.UtcNow
            };
            _context.DeletedEntities.Add(tombstone);
        }

        _context.TodoList.Remove(listToDelete);
        await _context.SaveChangesAsync();

        // Now run sync
        var result = await _syncService.SyncToRemoteAsync();

        // Assert - No deletions should occur
        Assert.Equal(0, result.ListsDeleted);
        Assert.Equal(0, result.ItemsDeleted);

        // Verify no tombstones exist
        var tombstones = await _context.DeletedEntities.ToListAsync();
        Assert.Empty(tombstones);

        // Verify DELETE was never called
        _mockExternalClient.Verify(
            x => x.DeleteTodoListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}
