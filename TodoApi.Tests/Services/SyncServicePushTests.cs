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
    private readonly Mock<ILogger<SyncServicePull>> _mockLogger;
    private readonly SyncServicePull _syncServicePull;

    public SyncServicePushTests()
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TodoContext(options);
        _mockExternalClient = new Mock<IExternalTodoApiClient>();
        _mockLogger = new Mock<ILogger<SyncServicePull>>();

        _syncServicePull = new SyncServicePull(_mockExternalClient.Object, _context, _mockLogger.Object);
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
        var result = await _syncServicePull.SyncToRemoteAsync();

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
        var result = await _syncServicePull.SyncToRemoteAsync();

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
        var result = await _syncServicePull.SyncToRemoteAsync();

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
        var result = await _syncServicePull.SyncToRemoteAsync();

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
        var result = await _syncServicePull.SyncToRemoteAsync();

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
        var result = await _syncServicePull.FullSyncAsync();

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
}
