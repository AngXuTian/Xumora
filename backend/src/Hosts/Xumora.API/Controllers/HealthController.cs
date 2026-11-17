using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xumora.Infrastructure.Shared.Persistence;
using Asp.Versioning;
using Xumora.API.Models;

namespace Xumora.API.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health")]
[ApiController]
public class HealthController : ControllerBase
{
    private readonly XumoraDbContext _dbContext;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        XumoraDbContext dbContext,
        ILogger<HealthController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("live")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Live()
    {
        return new HealthResponse("Healthy", Service: "Xumora.API");
    }

    [HttpGet("ready")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Ready(
        CancellationToken cancellationToken)
    {
        try
        {
            var connected = await _dbContext.Database
                .CanConnectAsync(cancellationToken);

            if (!connected)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new HealthResponse("Unhealthy", Database: "Disconnected"));
            }

            return new HealthResponse("Healthy", Database: "Connected");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Database readiness probe failed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new HealthResponse("Unhealthy", Database: "Unavailable"));
        }
    }
}