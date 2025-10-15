namespace TodoApi.Services;

public interface IBackgroundJobQueue
{
    void QueueJob(CompleteAllTodosJob job);
    Task<CompleteAllTodosJob> DequeueAsync(CancellationToken cancellationToken);
    JobStatus? GetJobStatus(string jobId);
    void UpdateJobStatus(string jobId, JobStatus status);
}
