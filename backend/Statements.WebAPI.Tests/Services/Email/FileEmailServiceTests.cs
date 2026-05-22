using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Statements.WebAPI.Services.Email;

namespace Statements.WebAPI.Tests.Services.Email;

/// <summary>
/// Unit tests for <see cref="FileEmailService"/>.
/// </summary>
public sealed class FileEmailServiceTests : IDisposable
{
    private readonly FileEmailService _sut;
    private static readonly string EmailsDir = Path.Combine("Logs", "emails");

    public FileEmailServiceTests()
    {
        // Ensure clean state before each test
        if (Directory.Exists(EmailsDir))
        {
            Directory.Delete(EmailsDir, recursive: true);
        }

        var options = Options.Create(new EmailOptions
        {
            UseFileOutput = true,
            FromAddress = "noreply@bankstatements.app",
            FromName = "Bank Statements"
        });

        var loggerMock = new Mock<ILogger<FileEmailService>>();
        _sut = new FileEmailService(options, loggerMock.Object);
    }

    [Fact]
    public async Task SendAsync_WritesEmlFileToDisk()
    {
        await _sut.SendAsync("test@example.com", "Test Subject", "Hello World");

        var files = Directory.GetFiles(EmailsDir, "*.eml");
        files.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SendAsync_EmlFileContainsHeadersAndBody()
    {
        await _sut.SendAsync("user@example.com", "Welcome!", "Thank you for registering.");

        var file = Directory.GetFiles(EmailsDir, "*.eml").Single();
        var content = await File.ReadAllTextAsync(file);

        content.Should().Contain("To: user@example.com");
        content.Should().Contain("Subject: Welcome!");
        content.Should().Contain("Thank you for registering.");
    }

    [Fact]
    public async Task SendAsync_HandlesSpecialCharactersInBody()
    {
        var specialBody = "Hello! <test> & \"quotes\" £ 100";
        await _sut.SendAsync("user@example.com", "Special", specialBody);

        var file = Directory.GetFiles(EmailsDir, "*.eml").Single();
        var content = await File.ReadAllTextAsync(file);

        content.Should().Contain(specialBody);
    }

    public void Dispose()
    {
        if (Directory.Exists(EmailsDir))
        {
            try { Directory.Delete(EmailsDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }
}
