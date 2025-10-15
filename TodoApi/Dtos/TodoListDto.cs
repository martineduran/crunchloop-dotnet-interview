namespace TodoApi.Dtos;

public class TodoListDto
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public int IncompleteItemCount { get; set; }
}
