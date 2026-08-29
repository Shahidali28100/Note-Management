namespace NoteManagement.Application.DTOs.Health;

/// <summary>
/// Matches the <c>HealthResponse</c> schema in this ticket's <c>delta-openapi.yaml</c> exactly.
/// </summary>
public sealed record HealthCheckResultDto(string Status, DateTime TimestampUtc);
