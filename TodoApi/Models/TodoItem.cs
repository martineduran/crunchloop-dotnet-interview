namespace TodoApi.Models;

public class TodoItem
{
    public long Id { get; set; }
    public required string Description { get; set; }
    public bool Completed { get; set; }

    public void Update(string name, bool completed)
    {
        Description = name;
        Completed = completed;
    }
}
