using TodoApi.Services;

namespace TodoApi.Tests.Controllers;

public class CompleteAllTodosTests
{
    [Fact]
    public void BackgroundJobQueue_QueuesAndDequeuesJobs()
    {
        // Arrange
        const string jobId = "test-job-1";
        var queue = new BackgroundJobQueue();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = 1 };

        // Act
        queue.QueueJob(job);
        var status = queue.GetJobStatus(jobId);

        // Assert
        Assert.NotNull(status);
        Assert.Equal(jobId, status.JobId);
        Assert.Equal(JobState.Queued, status.State);
    }

    [Fact]
    public void BackgroundJobQueue_UpdatesJobStatus()
    {
        // Arrange
        const string jobId = "test-job-2";
        var queue = new BackgroundJobQueue();
        var job = new CompleteAllTodosJob { JobId = jobId, TodoListId = 1 };

        // Act
        queue.QueueJob(job);
        var newStatus = new JobStatus
        {
            JobId = jobId,
            State = JobState.Processing,
            ProcessedCount = 5,
            TotalCount = 10,
            CreatedAt = DateTime.UtcNow,
        };
        queue.UpdateJobStatus(jobId, newStatus);

        var retrievedStatus = queue.GetJobStatus(jobId);

        // Assert
        Assert.NotNull(retrievedStatus);
        Assert.Equal(JobState.Processing, retrievedStatus.State);
        Assert.Equal(5, retrievedStatus.ProcessedCount);
        Assert.Equal(10, retrievedStatus.TotalCount);
    }

    [Fact]
    public void BackgroundJobQueue_ReturnsNullForNonexistentJob()
    {
        // Arrange
        var queue = new BackgroundJobQueue();

        // Act
        var status = queue.GetJobStatus("nonexistent-job");

        // Assert
        Assert.Null(status);
    }
}
