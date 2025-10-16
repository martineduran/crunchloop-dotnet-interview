using System.Text.Json.Serialization;

namespace TodoApi.Dtos.External;

public record UpdateTodoListRequestDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
