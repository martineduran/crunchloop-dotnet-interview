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
            var todoLists = JsonSerializer.Deserialize<List<ExternalTodoListDto>>(content, _jsonOptions)
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

    public async Task<ExternalTodoListDto> CreateTodoListAsync(
        CreateTodoListRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation("Creating TodoList on external API: {Name}", request.Name);

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync("/todolists", content, cancellationToken)
            );

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var createdList = JsonSerializer.Deserialize<ExternalTodoListDto>(responseContent, _jsonOptions)
                              ?? throw new InvalidOperationException("External API returned null response");

            _logger.LogInformation(
                "Successfully created TodoList on external API: {Name} (RemoteId: {RemoteId})",
                createdList.Name,
                createdList.Id
            );

            return createdList;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while creating TodoList on external API");
            throw new InvalidOperationException("Failed to create TodoList on external API", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error while creating TodoList on external API");
            throw new InvalidOperationException("Failed to parse external API response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating TodoList on external API");
            throw;
        }
    }

    public async Task<ExternalTodoListDto> UpdateTodoListAsync(
        string todolistId,
        UpdateTodoListRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation("Updating TodoList on external API: {TodoListId}", todolistId);

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PatchAsync($"/todolists/{todolistId}", content, cancellationToken)
            );

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var updatedList = JsonSerializer.Deserialize<ExternalTodoListDto>(responseContent, _jsonOptions)
                              ?? throw new InvalidOperationException("External API returned null response");

            _logger.LogInformation("Successfully updated TodoList on external API: {TodoListId}", todolistId);

            return updatedList;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while updating TodoList {TodoListId} on external API", todolistId);
            throw new InvalidOperationException("Failed to update TodoList on external API", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error while updating TodoList {TodoListId} on external API", todolistId);
            throw new InvalidOperationException("Failed to parse external API response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating TodoList {TodoListId} on external API", todolistId);
            throw;
        }
    }

    public async Task DeleteTodoListAsync(
        string todolistId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation("Deleting TodoList on external API: {TodoListId}", todolistId);

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.DeleteAsync($"/todolists/{todolistId}", cancellationToken)
            );

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully deleted TodoList on external API: {TodoListId}", todolistId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while deleting TodoList {TodoListId} on external API", todolistId);
            throw new InvalidOperationException("Failed to delete TodoList on external API", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deleting TodoList {TodoListId} on external API", todolistId);
            throw;
        }
    }

    public async Task<ExternalTodoItemDto> UpdateTodoItemAsync(
        string todolistId,
        string todoitemId,
        UpdateTodoItemRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation(
                "Updating TodoItem on external API: {TodoItemId} in list {TodoListId}",
                todoitemId,
                todolistId
            );

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PatchAsync(
                    $"/todolists/{todolistId}/todoitems/{todoitemId}",
                    content,
                    cancellationToken
                )
            );

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var updatedItem = JsonSerializer.Deserialize<ExternalTodoItemDto>(responseContent, _jsonOptions)
                              ?? throw new InvalidOperationException("External API returned null response");

            _logger.LogInformation("Successfully updated TodoItem on external API: {TodoItemId}", todoitemId);

            return updatedItem;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while updating TodoItem {TodoItemId} on external API", todoitemId);
            throw new InvalidOperationException("Failed to update TodoItem on external API", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON error while updating TodoItem {TodoItemId} on external API", todoitemId);
            throw new InvalidOperationException("Failed to parse external API response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while updating TodoItem {TodoItemId} on external API", todoitemId);
            throw;
        }
    }

    public async Task DeleteTodoItemAsync(
        string todolistId,
        string todoitemId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation(
                "Deleting TodoItem on external API: {TodoItemId} from list {TodoListId}",
                todoitemId,
                todolistId
            );

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.DeleteAsync(
                    $"/todolists/{todolistId}/todoitems/{todoitemId}",
                    cancellationToken
                )
            );

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Successfully deleted TodoItem on external API: {TodoItemId}", todoitemId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while deleting TodoItem {TodoItemId} on external API", todoitemId);
            throw new InvalidOperationException("Failed to delete TodoItem on external API", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while deleting TodoItem {TodoItemId} on external API", todoitemId);
            throw;
        }
    }
}
