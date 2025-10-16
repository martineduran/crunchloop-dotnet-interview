using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public record CreateTodoItemRequestDto
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("completed")]
    public bool Completed { get; set; }
}
