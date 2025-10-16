using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public record CreateTodoListRequestDto
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("items")]
    public List<CreateTodoItemRequestDto> Items { get; set; } = [];
}
