using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Statements.WebAPI.Services.Email;

namespace Statements.WebAPI.Tests.Services.Email;

/// <summary>
/// Unit tests for <see cref="SmtpEmailService"/>.
/// </summary>
public sealed class SmtpEmailServiceTests
{
    private readonly Mock<ILogger<SmtpEmailService>> _loggerMock = new();

    [Fact]
    public async Task SendAsync_ThrowsOnUnreachableHost()
    {
        var options = Options.Create(new EmailOptions
        {
            UseFileOutput = false,
            FromAddress = "noreply@bankstatements.app",
            FromName = "Bank Statements",
            SmtpHost = "192.0.2.1", // TEST-NET address guaranteed unreachable
            SmtpPort = 25,
            SmtpUsername = "",
            SmtpPassword = ""
        });

        var sut = new SmtpEmailService(options, _loggerMock.Object);

        var act = () => sut.SendAsync("test@example.com", "Test", "Body");

        // Should fail to connect to unreachable host
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Constructor_WithValidOptions_DoesNotThrow()
    {
        var options = Options.Create(new EmailOptions
        {
            UseFileOutput = false,
            FromAddress = "noreply@bankstatements.app",
            FromName = "Bank Statements",
            SmtpHost = "localhost",
            SmtpPort = 587
        });

        var sut = new SmtpEmailService(options, _loggerMock.Object);

        sut.Should().NotBeNull();
    }
}
