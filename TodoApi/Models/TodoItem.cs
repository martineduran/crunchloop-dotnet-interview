namespace TodoApi.Models;

public class TodoItem
{
    public long Id { get; set; }
    public required string Description { get; set; }
    public bool Completed { get; set; }
    public long? TodoListId { get; set; }

    // Sync metadata
    public string? RemoteId { get; set; }
    public string? SourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    public void Update(string name, bool completed)
    {
        Description = name;
        Completed = completed;
        UpdatedAt = DateTime.UtcNow;
    }
}
