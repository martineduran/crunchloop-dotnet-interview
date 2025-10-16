namespace TodoApi.Dtos.Sync;

public record SyncResultDto
{
    public int ListsCreated { get; set; }
    public int ListsUpdated { get; set; }
    public int ListsSkipped { get; set; }
    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsSkipped { get; set; }
    public List<string> Errors { get; set; } = [];
    public DateTime SyncCompletedAt { get; set; }
    public bool Success => Errors.Count == 0;
}
