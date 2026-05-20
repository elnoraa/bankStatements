using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Controllers;

namespace Statements.WebAPI.Tests.Controllers;

/// <summary>
/// Unit tests for the <see cref="HealthController"/>.
/// </summary>
public sealed class HealthControllerTests
{
    /// <summary>
    /// Verifies that the health endpoint returns 200 OK with status "ok" and a timestamp.
    /// </summary>
    [Fact]
    public void Get_ReturnsOkWithStatusAndTimestamp()
    {
        var logger = Mock.Of<ILogger<HealthController>>();
        var controller = new HealthController(logger);

        var result = controller.Get();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value!;
        var status = value.GetType().GetProperty("status")!.GetValue(value)!.ToString();
        status.Should().Be("ok");
        value.GetType().GetProperty("checkedAt").Should().NotBeNull();
    }
}
