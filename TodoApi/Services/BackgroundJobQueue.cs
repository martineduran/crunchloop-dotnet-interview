using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TodoApi.Services;

public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<CompleteAllTodosJob> _queue;
    private readonly ConcurrentDictionary<string, JobStatus> _jobStatuses;

    public BackgroundJobQueue(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait };
        _queue = Channel.CreateBounded<CompleteAllTodosJob>(options);
        _jobStatuses = new ConcurrentDictionary<string, JobStatus>();
    }

    public void QueueJob(CompleteAllTodosJob job)
    {
        if (!_queue.Writer.TryWrite(job))
        {
            throw new InvalidOperationException("Job queue is full");
        }

        _jobStatuses[job.JobId] = new JobStatus
        {
            JobId = job.JobId,
            State = JobState.Queued,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public async Task<CompleteAllTodosJob> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public JobStatus? GetJobStatus(string jobId)
    {
        return _jobStatuses.GetValueOrDefault(jobId);
    }

    public void UpdateJobStatus(string jobId, JobStatus status)
    {
        _jobStatuses[jobId] = status;
    }
}
