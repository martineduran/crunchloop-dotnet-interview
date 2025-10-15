namespace TodoApi.Services;

public record CompleteAllTodosJob
{
    public required string JobId { get; set; }
    public required long TodoListId { get; set; }
}
