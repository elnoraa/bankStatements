using Microsoft.AspNetCore.Mvc;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a simple health check response indicating the API is running.
    /// </summary>
    /// <returns>200 OK with status and current timestamp.</returns>
    [HttpGet]
    public IActionResult Get()
    {
        _logger.LogInformation("Health check requested");
        return Ok(new
        {
            status = "ok",
            checkedAt = DateTimeOffset.UtcNow
        });
    }
}
