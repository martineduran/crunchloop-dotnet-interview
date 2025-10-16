namespace TodoApi.Models;

public class TodoList
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public List<TodoItem> TodoItems { get; set; } = [];

    // Sync metadata
    public string? RemoteId { get; set; }
    public string? SourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
