using Microsoft.AspNetCore.Mvc;
using TodoApi.Dtos;
using TodoApi.Services;

namespace TodoApi.Controllers;

public class JobsController : ControllerBase
{
    private readonly IBackgroundJobQueue _jobQueue;

    public JobsController(IBackgroundJobQueue jobQueue)
    {
        _jobQueue = jobQueue;
    }
    
    // GET: api/todolists/5/jobs/{jobId}/status
    [HttpGet("{id:long}/jobs/{jobId}/status")]
    public ActionResult<JobStatusDto> GetJobStatus(long id, string jobId)
    {
        var status = _jobQueue.GetJobStatus(jobId);
        if (status == null)
        {
            return NotFound();
        }

        var dto = new JobStatusDto(
            status.JobId,
            status.State,
            status.ProcessedCount,
            status.TotalCount,
            status.ErrorMessage,
            status.CreatedAt,
            status.CompletedAt
        );

        return Ok(dto);
    }
}
