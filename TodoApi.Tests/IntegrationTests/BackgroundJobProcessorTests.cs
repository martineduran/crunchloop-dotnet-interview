using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TodoApi.Data;
using TodoApi.Hubs;
using TodoApi.Models;
using TodoApi.Services;

namespace TodoApi.Tests.IntegrationTests;

public class BackgroundJobProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly IBackgroundJobQueue _jobQueue;
    private readonly BackgroundJobProcessor _processor;

    public BackgroundJobProcessorTests()
    {
        // Setup in-memory database
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<TodoContext>(options =>
            options.UseInMemoryDatabase(databaseName)
        );

        _serviceProvider = services.BuildServiceProvider();

        // Setup mock SignalR hub
        var mockHubContext = new Mock<IHubContext<TodoProgressHub>>();
        var mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        mockHubContext.Setup(x => x.Clients).Returns(mockClients.Object);
        mockClients.Setup(x => x.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);

        // Setup job queue and processor
        _jobQueue = new BackgroundJobQueue();
        _processor = new BackgroundJobProcessor(
            _jobQueue,
            _serviceProvider,
            mockHubContext.Object,
            NullLogger<BackgroundJobProcessor>.Instance
        );
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    private async Task<TodoList> CreateTodoListWithItems(int incompleteCount, int completedCount)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();

        var todoList = new TodoList
        {
            Name = "Test List",
            TodoItems = [],
        };

        for (var i = 0; i < incompleteCount; i++)
        {
            todoList.TodoItems.Add(new TodoItem
            {
                Description = $"Incomplete Task {i + 1}",
                Completed = false,
            });
        }

        for (var i = 0; i < completedCount; i++)
        {
            todoList.TodoItems.Add(new TodoItem
            {
                Description = $"Completed Task {i + 1}",
                Completed = true,
            });
        }

        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        return todoList;
    }

    private async Task RunProcessorUntilJobCompleted(string jobId, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var processorTask = _processor.StartAsync(cts.Token);

        // Wait until job is completed or failed
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < TimeSpan.FromMilliseconds(timeoutMs))
        {
            var status = _jobQueue.GetJobStatus(jobId);
            if (status?.State is JobState.Completed or JobState.Failed)
            {
                break;
            }
            await Task.Delay(50, cts.Token);
        }

        await cts.CancelAsync();

        try
        {
            await processorTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [Fact]
    public async Task ProcessJob_CompletesAllIncompleteTodos()
    {
        // Arrange
        var todoList = await CreateTodoListWithItems(incompleteCount: 3, completedCount: 2);
        var jobId = Guid.NewGuid().ToString();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = todoList.Id };

        // Act
        _jobQueue.QueueJob(job);
        await RunProcessorUntilJobCompleted(jobId);

        // Assert
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
        var todos = await context.TodoItems.Where(x => x.TodoListId == todoList.Id).ToListAsync();

        Assert.Equal(5, todos.Count);
        Assert.All(todos, todo => Assert.True(todo.Completed));

        var finalStatus = _jobQueue.GetJobStatus(jobId);
        Assert.NotNull(finalStatus);
        Assert.Equal(JobState.Completed, finalStatus.State);
    }

    [Fact]
    public async Task ProcessJob_UpdatesJobStatusThroughLifecycle()
    {
        // Arrange
        var todoList = await CreateTodoListWithItems(incompleteCount: 5, completedCount: 0);
        var jobId = Guid.NewGuid().ToString();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = todoList.Id };

        // Act
        _jobQueue.QueueJob(job);
        var initialStatus = _jobQueue.GetJobStatus(jobId);

        await RunProcessorUntilJobCompleted(jobId);
        var finalStatus = _jobQueue.GetJobStatus(jobId);

        // Assert
        Assert.NotNull(initialStatus);
        Assert.Equal(JobState.Queued, initialStatus.State);

        Assert.NotNull(finalStatus);
        Assert.Equal(JobState.Completed, finalStatus.State);
        Assert.NotNull(finalStatus.CompletedAt);
    }

    [Fact]
    public async Task ProcessJob_TracksProgressCorrectly()
    {
        // Arrange - Create 12 items (batch size is 5, so 3 batches)
        const int itemCount = 12;
        var todoList = await CreateTodoListWithItems(incompleteCount: itemCount, completedCount: 0);
        var jobId = Guid.NewGuid().ToString();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = todoList.Id };

        // Act
        _jobQueue.QueueJob(job);
        await RunProcessorUntilJobCompleted(jobId);

        // Assert
        var finalStatus = _jobQueue.GetJobStatus(jobId);
        Assert.NotNull(finalStatus);
        Assert.Equal(JobState.Completed, finalStatus.State);
        Assert.Equal(itemCount, finalStatus.TotalCount);
        Assert.Equal(itemCount, finalStatus.ProcessedCount);
    }

    [Fact]
    public async Task ProcessJob_HandlesEmptyTodoList()
    {
        // Arrange
        var todoList = await CreateTodoListWithItems(incompleteCount: 0, completedCount: 0);
        var jobId = Guid.NewGuid().ToString();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = todoList.Id };

        // Act
        _jobQueue.QueueJob(job);
        await RunProcessorUntilJobCompleted(jobId);

        // Assert
        var finalStatus = _jobQueue.GetJobStatus(jobId);
        Assert.NotNull(finalStatus);
        Assert.Equal(JobState.Completed, finalStatus.State);
        Assert.Equal(0, finalStatus.TotalCount);
        Assert.Equal(0, finalStatus.ProcessedCount);
    }

    [Fact]
    public async Task ProcessJob_HandlesAllAlreadyCompleted()
    {
        // Arrange
        var todoList = await CreateTodoListWithItems(incompleteCount: 0, completedCount: 5);
        var jobId = Guid.NewGuid().ToString();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = todoList.Id };

        // Act
        _jobQueue.QueueJob(job);
        await RunProcessorUntilJobCompleted(jobId);

        // Assert
        var finalStatus = _jobQueue.GetJobStatus(jobId);
        Assert.NotNull(finalStatus);
        Assert.Equal(JobState.Completed, finalStatus.State);
        Assert.Equal(0, finalStatus.TotalCount); // No incomplete items
        Assert.Equal(0, finalStatus.ProcessedCount);
    }

    [Fact]
    public async Task ProcessJob_SendsSignalRUpdates()
    {
        // Arrange
        var todoList = await CreateTodoListWithItems(incompleteCount: 7, completedCount: 0);
        var jobId = Guid.NewGuid().ToString();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = todoList.Id };

        // Act
        _jobQueue.QueueJob(job);
        await RunProcessorUntilJobCompleted(jobId);

        // Assert
        // Verify that SignalR SendCoreAsync was called at least once
        _mockClientProxy.Verify(
            x => x.SendCoreAsync(
                "JobStatusUpdate",
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()
            ),
            Times.AtLeastOnce
        );
    }
}
