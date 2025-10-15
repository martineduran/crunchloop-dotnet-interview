using TodoApi.Services;

namespace TodoApi.Dtos;

public record JobStatusDto(
    string JobId,
    JobState State,
    int ProcessedCount,
    int TotalCount,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt
);
