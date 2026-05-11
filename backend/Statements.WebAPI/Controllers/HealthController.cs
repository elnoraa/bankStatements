using Microsoft.AspNetCore.Mvc;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            checkedAt = DateTimeOffset.UtcNow
        });
    }
}
