namespace TodoApi.Configuration;

public record ExternalTodoApiConfiguration
{
    public required string BaseUrl { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int RetryCount { get; init; } = 3;
    public int RetryDelaySeconds { get; init; } = 2;
}
