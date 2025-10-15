namespace TodoApi.Services;

public record JobStatus
{
    public required string JobId { get; set; }
    public required JobState State { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}