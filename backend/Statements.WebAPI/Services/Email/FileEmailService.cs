using System.Text;
using Microsoft.Extensions.Options;

namespace Statements.WebAPI.Services.Email;

/// <summary>
/// Writes emails to disk as .eml files in the Logs/emails/ directory.
/// Used in development environments where no SMTP server is available.
/// </summary>
public sealed class FileEmailService : IEmailService
{
    private readonly string _outputDirectory;
    private readonly ILogger<FileEmailService> _logger;

    public FileEmailService(
        IOptions<EmailOptions> emailOptions,
        ILogger<FileEmailService> logger)
    {
        _outputDirectory = Path.Combine("Logs", "emails");
        _logger = logger;
        Directory.CreateDirectory(_outputDirectory);
    }

    /// <inheritdoc />
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var safeEmail = to.Replace("@", "_at_").Replace(".", "_dot_");
        var fileName = $"{timestamp}-{safeEmail}.eml";
        var filePath = Path.Combine(_outputDirectory, fileName);

        var emlContent = new StringBuilder();
        emlContent.AppendLine("From: Bank Statements <noreply@bankstatements.app>");
        emlContent.AppendLine($"To: {to}");
        emlContent.AppendLine($"Subject: {subject}");
        emlContent.AppendLine("MIME-Version: 1.0");
        emlContent.AppendLine("Content-Type: text/plain; charset=\"utf-8\"");
        emlContent.AppendLine();
        emlContent.Append(body);

        await File.WriteAllTextAsync(filePath, emlContent.ToString(), Encoding.UTF8, cancellationToken);

        _logger.LogInformation("Email written to file: {FilePath} (To: {To}, Subject: {Subject})",
            filePath, to, subject);
    }
}
