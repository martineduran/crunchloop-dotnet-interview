using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TodoApi.Data;
using TodoApi.Dtos.External;
using TodoApi.Models;
using TodoApi.Services.Sync;

namespace TodoApi.Tests.Services;

public class SyncServicePullTests : IDisposable
{
    private readonly TodoContext _context;
    private readonly Mock<IExternalTodoApiClient> _mockExternalClient;
    private readonly Mock<ILogger<SyncService>> _mockLogger;
    private readonly SyncService _syncService;

    public SyncServicePullTests()
    {
        // Setup in-memory database
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
    public async Task SyncFromRemoteAsync_CreatesNewTodoList_WhenNotExists()
    {
        // Arrange
        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "Test List",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                Items = [],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(1, result.ListsCreated);
        Assert.Equal(0, result.ListsUpdated);
        Assert.Equal(0, result.Errors.Count);

        var localList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(localList);
        Assert.Equal("Test List", localList.Name);
        Assert.Equal("remote-1", localList.RemoteId);
        Assert.Equal("source-1", localList.SourceId);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_UpdatesExistingTodoList_WhenRemoteIsNewer()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var newTime = DateTime.UtcNow.AddHours(-1);

        var localList = new TodoList
        {
            Name = "Old Name",
            RemoteId = "remote-1",
            SourceId = "source-1",
            CreatedAt = oldTime,
            UpdatedAt = oldTime,
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "New Name",
                CreatedAt = oldTime,
                UpdatedAt = newTime,
                Items = [],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(1, result.ListsUpdated);
        Assert.Equal(0, result.Errors.Count);

        var updatedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Equal("New Name", updatedList.Name);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_SkipsUpdate_WhenLocalIsNewer()
    {
        // Arrange
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var newTime = DateTime.UtcNow.AddHours(-1);

        var localList = new TodoList
        {
            Name = "Local Name",
            RemoteId = "remote-1",
            SourceId = "source-1",
            CreatedAt = oldTime,
            UpdatedAt = newTime,
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "Remote Name",
                CreatedAt = oldTime,
                UpdatedAt = oldTime,
                Items = [],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(0, result.ListsUpdated);
        Assert.Equal(1, result.ListsSkipped);
        Assert.Equal(0, result.Errors.Count);

        var updatedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Equal("Local Name", updatedList.Name);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_CreatesNewTodoItem_WhenNotExists()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "Test List",
            RemoteId = "remote-1",
            SourceId = "source-1",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "Test List",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                Items =
                [
                    new()
                    {
                        Id = "item-1",
                        SourceId = "item-source-1",
                        Description = "Test Item",
                        Completed = false,
                        CreatedAt = DateTime.UtcNow.AddHours(-1),
                        UpdatedAt = DateTime.UtcNow.AddHours(-1),
                    },
                ],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(1, result.ItemsCreated);
        Assert.Equal(0, result.ItemsUpdated);
        Assert.Equal(0, result.Errors.Count);

        var updatedList = await _context.TodoList.Include(l => l.TodoItems).FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Single(updatedList.TodoItems);
        Assert.Equal("Test Item", updatedList.TodoItems.First().Description);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_MatchesBySourceId_WhenRemoteIdDiffers()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "Test List",
            SourceId = "source-1",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "Updated Name",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                Items = [],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(1, result.ListsUpdated);
        Assert.Equal(0, result.Errors.Count);

        var updatedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Equal("Updated Name", updatedList.Name);
        Assert.Equal("remote-1", updatedList.RemoteId);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_MatchesByName_WhenNoIds()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "Test List",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "Test List",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                Items = [],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(1, result.ListsUpdated);
        Assert.Equal(0, result.Errors.Count);

        var updatedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Equal("remote-1", updatedList.RemoteId);
        Assert.Equal("source-1", updatedList.SourceId);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_DeletesLocalTodoList_WhenRemovedRemotely()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "List To Delete",
            RemoteId = "remote-1",
            SourceId = "source-1",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            TodoItems = [],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        // Remote returns empty list (list was deleted remotely)
        var remoteLists = new List<ExternalTodoListDto>();

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(0, result.ListsUpdated);
        Assert.Equal(1, result.ListsDeleted);
        Assert.Equal(0, result.Errors.Count);

        var deletedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.Null(deletedList);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_DeletesLocalTodoListWithItems_WhenRemovedRemotely()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "List With Items",
            RemoteId = "remote-1",
            SourceId = "source-1",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            TodoItems =
            [
                new TodoItem
                {
                    Description = "Item 1",
                    Completed = false,
                    RemoteId = "item-1",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-2),
                },
                new TodoItem
                {
                    Description = "Item 2",
                    Completed = true,
                    RemoteId = "item-2",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-2),
                },
            ],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        // Remote returns empty list (list with items was deleted remotely)
        var remoteLists = new List<ExternalTodoListDto>();

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(0, result.ListsUpdated);
        Assert.Equal(1, result.ListsDeleted);
        Assert.Equal(2, result.ItemsDeleted); // Cascade deletion
        Assert.Equal(0, result.Errors.Count);

        var deletedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.Null(deletedList);

        var deletedItems = await _context.TodoItems.ToListAsync();
        Assert.Empty(deletedItems);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_DeletesLocalTodoItem_WhenRemovedRemotely()
    {
        // Arrange
        var localList = new TodoList
        {
            Name = "Test List",
            RemoteId = "remote-1",
            SourceId = "source-1",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            TodoItems =
            [
                new TodoItem
                {
                    Description = "Item To Delete",
                    Completed = false,
                    RemoteId = "item-1",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-2),
                },
                new TodoItem
                {
                    Description = "Item To Keep",
                    Completed = false,
                    RemoteId = "item-2",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-2),
                },
            ],
        };

        _context.TodoList.Add(localList);
        await _context.SaveChangesAsync();

        // Remote returns list with only one item (item-2)
        var remoteLists = new List<ExternalTodoListDto>
        {
            new()
            {
                Id = "remote-1",
                SourceId = "source-1",
                Name = "Test List",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                UpdatedAt = DateTime.UtcNow.AddHours(-2),
                Items =
                [
                    new()
                    {
                        Id = "item-2",
                        Description = "Item To Keep",
                        Completed = false,
                        CreatedAt = DateTime.UtcNow.AddHours(-2),
                        UpdatedAt = DateTime.UtcNow.AddHours(-2),
                    },
                ],
            },
        };

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(0, result.ListsDeleted);
        Assert.Equal(0, result.ItemsCreated);
        Assert.Equal(1, result.ItemsDeleted); // Item-1 deleted
        Assert.Empty(result.Errors);

        // Verify the correct item remains
        var updatedList = await _context.TodoList.Include(l => l.TodoItems).FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Single(updatedList.TodoItems);
        Assert.Equal("Item To Keep", updatedList.TodoItems.First().Description);
        Assert.Equal("item-2", updatedList.TodoItems.First().RemoteId);
    }

    [Fact]
    public async Task SyncFromRemoteAsync_PreservesLocalOnlyEntities_WhenNoRemoteId()
    {
        // Arrange - Local entity without RemoteId (never synced)
        var localOnlyList = new TodoList
        {
            Name = "Local Only List",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            TodoItems =
            [
                new TodoItem
                {
                    Description = "Local Only Item",
                    Completed = false,
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-2),
                },
            ],
        };

        _context.TodoList.Add(localOnlyList);
        await _context.SaveChangesAsync();

        // Remote returns empty list
        var remoteLists = new List<ExternalTodoListDto>();

        _mockExternalClient
            .Setup(x => x.GetAllTodoListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remoteLists);

        // Act
        var result = await _syncService.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsDeleted);
        Assert.Equal(0, result.ItemsDeleted);
        Assert.Equal(0, result.Errors.Count);

        // Local-only entities should be preserved
        var localList = await _context.TodoList.Include(l => l.TodoItems).FirstOrDefaultAsync();
        Assert.NotNull(localList);
        Assert.Equal("Local Only List", localList.Name);
        Assert.Single(localList.TodoItems);
        Assert.Equal("Local Only Item", localList.TodoItems.First().Description);
    }
}
