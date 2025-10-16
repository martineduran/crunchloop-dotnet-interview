using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TodoApi.Configuration;
using TodoApi.Dtos.External;

namespace TodoApi.Services.Sync;

public class ExternalTodoApiClient : IExternalTodoApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalTodoApiClient> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExternalTodoApiClient(
        HttpClient httpClient,
        IOptions<ExternalTodoApiConfiguration> configuration,
        ILogger<ExternalTodoApiClient> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;

        var config = configuration.Value;

        _httpClient.BaseAddress = new Uri(config.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        // Configure retry policy with exponential backoff
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                config.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(
                    config.RetryDelaySeconds * Math.Pow(2, retryAttempt - 1)
                ),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var statusCode = outcome.Result?.StatusCode.ToString() ?? "N/A";
                    _logger.LogWarning(
                        "Retry {RetryCount} after {Delay}s due to {StatusCode}. Exception: {Exception}",
                        retryCount,
                        timespan.TotalSeconds,
                        statusCode,
                        outcome.Exception?.Message
                    );
                }
            );

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    public async Task<List<ExternalTodoListDto>> GetAllTodoListsAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation("Fetching all TodoLists from external API");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.GetAsync("/todolists", cancellationToken)
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var todoLists =
                JsonSerializer.Deserialize<List<ExternalTodoListDto>>(content, _jsonOptions)
                ?? [];

            _logger.LogInformation(
                "Successfully fetched {Count} TodoLists from external API",
                todoLists.Count
            );

            return todoLists;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while fetching TodoLists from external API");
            throw new InvalidOperationException(
                "Failed to fetch TodoLists from external API",
                ex
            );
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization error while parsing external API response");
            throw new InvalidOperationException(
                "Failed to parse external API response",
                ex
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while fetching TodoLists from external API");
            throw;
        }
    }
}
