using NoteManagement.Application.DTOs.Health;

namespace NoteManagement.Application.Interfaces;

public interface IHealthCheckService
{
    Task<HealthCheckResultDto> CheckAsync(CancellationToken cancellationToken);
}
