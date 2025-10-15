using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Hubs;

namespace TodoApi.Services;

public class BackgroundJobProcessor : BackgroundService
{
    private readonly IBackgroundJobQueue _jobQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<TodoProgressHub> _hubContext;
    private readonly ILogger<BackgroundJobProcessor> _logger;

    public BackgroundJobProcessor(
        IBackgroundJobQueue jobQueue,
        IServiceProvider serviceProvider,
        IHubContext<TodoProgressHub> hubContext,
        ILogger<BackgroundJobProcessor> logger
    )
    {
        _jobQueue = jobQueue;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background Job Processor is starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobQueue.DequeueAsync(stoppingToken);
                await ProcessCompleteAllTodosJob(job, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation token is triggered
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing background job");
            }
        }

        _logger.LogInformation("Background Job Processor is stopping");
    }

    private async Task ProcessCompleteAllTodosJob(
        CompleteAllTodosJob job,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _logger.LogInformation(
                "Processing CompleteAllTodos job {JobId} for TodoList {TodoListId}",
                job.JobId,
                job.TodoListId
            );

            // Update status to Processing
            var status = new JobStatus
            {
                JobId = job.JobId,
                State = JobState.Processing,
                CreatedAt = DateTime.UtcNow,
                ProcessedCount = 0,
                TotalCount = 0,
            };
            _jobQueue.UpdateJobStatus(job.JobId, status);

            await _hubContext.Clients.Group(job.JobId).SendAsync(
                "JobStatusUpdate",
                status,
                cancellationToken
            );

            // Create a new scope to get DbContext
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
            
            var totalPendingCount = await context
                .TodoItems
                .Where(x => x.TodoListId == job.TodoListId && !x.Completed)
                .CountAsync(cancellationToken);

            status.TotalCount = totalPendingCount;
            _jobQueue.UpdateJobStatus(job.JobId, status);

            await _hubContext.Clients.Group(job.JobId).SendAsync(
                "JobStatusUpdate",
                status,
                cancellationToken
            );

            // Process in batches
            const int batchSize = 5; // Small value for demo purposes only
            var processedCount = 0;

            while (true)
            {
                var batch = await context
                    .TodoItems
                    .Where(x => x.TodoListId == job.TodoListId && !x.Completed)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                if (!batch.Any())
                    break;

                foreach (var item in batch)
                {
                    item.Completed = true;
                }

                await context.SaveChangesAsync(cancellationToken);
                processedCount += batch.Count;

                // Update progress
                status.ProcessedCount = processedCount;
                _jobQueue.UpdateJobStatus(job.JobId, status);

                await _hubContext.Clients.Group(job.JobId).SendAsync(
                    "JobStatusUpdate",
                    status,
                    cancellationToken
                );

                _logger.LogInformation(
                    "Job {JobId}: Processed {ProcessedCount}/{TotalCount}",
                    job.JobId,
                    processedCount,
                    totalPendingCount
                );
                
                await Task.Delay(100, cancellationToken);
            }

            // Mark as completed
            status.State = JobState.Completed;
            status.CompletedAt = DateTime.UtcNow;
            _jobQueue.UpdateJobStatus(job.JobId, status);

            await _hubContext.Clients.Group(job.JobId).SendAsync(
                "JobStatusUpdate",
                status,
                cancellationToken
            );

            _logger.LogInformation("Job {JobId} completed successfully", job.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId}", job.JobId);

            var errorStatus = new JobStatus
            {
                JobId = job.JobId,
                State = JobState.Failed,
                ErrorMessage = ex.Message,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            };
            _jobQueue.UpdateJobStatus(job.JobId, errorStatus);

            await _hubContext.Clients.Group(job.JobId).SendAsync(
                "JobStatusUpdate",
                errorStatus,
                cancellationToken
            );
        }
    }
}
