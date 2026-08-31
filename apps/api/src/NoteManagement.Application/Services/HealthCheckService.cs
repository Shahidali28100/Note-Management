using NoteManagement.Application.DTOs.Health;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Application.Services;

public sealed class HealthCheckService : IHealthCheckService
{
    private readonly IDatabaseHealthChecker _databaseHealthChecker;

    public HealthCheckService(IDatabaseHealthChecker databaseHealthChecker)
    {
        _databaseHealthChecker = databaseHealthChecker;
    }

    /// <summary>
    /// Throws rather than returning a "degraded" status when the database is unreachable,
    /// because this ticket's <c>delta-openapi.yaml</c> <c>HealthResponse.status</c> enum only
    /// defines <c>healthy</c> — there is no contract value for "unhealthy". An unreachable
    /// database is therefore a 500 (Problem Details, via the global exception handler), not a
    /// 200 with an undocumented body shape.
    /// </summary>
    public async Task<HealthCheckResultDto> CheckAsync(CancellationToken cancellationToken)
    {
        var canConnect = await _databaseHealthChecker.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            throw new InvalidOperationException("Database is not reachable.");
        }

        return new HealthCheckResultDto("healthy", DateTime.UtcNow);
    }
}
