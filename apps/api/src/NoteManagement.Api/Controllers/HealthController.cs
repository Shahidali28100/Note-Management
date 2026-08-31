using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoteManagement.Application.DTOs.Health;
using NoteManagement.Application.Interfaces;

namespace NoteManagement.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Scaffold-only endpoint (AB-1001) proving the API is running and its database
    /// provider is reachable. Not part of any FRS requirement. See delta-openapi.yaml.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HealthCheckResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HealthCheckResultDto>> Get(CancellationToken cancellationToken)
    {
        var result = await _healthCheckService.CheckAsync(cancellationToken);
        return Ok(result);
    }
}
