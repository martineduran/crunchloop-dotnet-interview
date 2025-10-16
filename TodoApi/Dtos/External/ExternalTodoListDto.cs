using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public record ExternalTodoListDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public List<ExternalTodoItemDto> Items { get; set; } = [];
}
