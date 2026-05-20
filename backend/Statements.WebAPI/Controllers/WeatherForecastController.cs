using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Models;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries =
    [
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching"
    ];

    /// <summary>
    /// Returns a 5-day weather forecast with random temperatures.
    /// </summary>
    /// <returns>An array of <see cref="WeatherForecast"/> objects.</returns>
    [HttpGet]
    public ActionResult<IEnumerable<WeatherForecast>> Get()
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                Summaries[Random.Shared.Next(Summaries.Length)]
            ))
            .ToArray();

        return Ok(forecast);
    }
}
