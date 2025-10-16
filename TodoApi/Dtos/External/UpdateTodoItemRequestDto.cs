using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public record UpdateTodoItemRequestDto
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("completed")]
    public bool? Completed { get; set; }
}
