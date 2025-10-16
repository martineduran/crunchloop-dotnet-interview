namespace TodoApi.Models;

public class DeletedEntity
{
    public long Id { get; set; }
    public required string RemoteId { get; set; }
    public required string EntityType { get; set; }
    public DateTime DeletedAt { get; set; }
    public string? ParentRemoteId { get; set; }
}
