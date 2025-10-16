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
    private readonly Mock<ILogger<SyncServicePull>> _mockLogger;
    private readonly SyncServicePull _syncServicePull;

    public SyncServicePullTests()
    {
        // Setup in-memory database
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
        var result = await _syncServicePull.SyncFromRemoteAsync();

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
        var result = await _syncServicePull.SyncFromRemoteAsync();

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
        var result = await _syncServicePull.SyncFromRemoteAsync();

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
        var result = await _syncServicePull.SyncFromRemoteAsync();

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
        var result = await _syncServicePull.SyncFromRemoteAsync();

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
        var result = await _syncServicePull.SyncFromRemoteAsync();

        // Assert
        Assert.Equal(0, result.ListsCreated);
        Assert.Equal(1, result.ListsUpdated);
        Assert.Equal(0, result.Errors.Count);

        var updatedList = await _context.TodoList.FirstOrDefaultAsync();
        Assert.NotNull(updatedList);
        Assert.Equal("remote-1", updatedList.RemoteId);
        Assert.Equal("source-1", updatedList.SourceId);
    }
}
