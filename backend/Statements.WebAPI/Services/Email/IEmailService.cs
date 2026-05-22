namespace Statements.WebAPI.Services.Email;

/// <summary>
/// Provides email sending capabilities for the application.
/// Used for transactional emails such as verification, password reset, and notifications.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to the specified recipient.
    /// </summary>
    /// <param name="to">The recipient email address.</param>
    /// <param name="subject">The email subject line.</param>
    /// <param name="body">The email body content (plain text or HTML depending on implementation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
