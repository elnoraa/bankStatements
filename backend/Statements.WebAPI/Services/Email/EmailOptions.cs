namespace Statements.WebAPI.Services.Email;

/// <summary>
/// Configuration options for email sending.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// When true, emails are written to the Logs/emails/ directory instead of being sent via SMTP.
    /// Useful for development and testing without a real mail server.
    /// </summary>
    public bool UseFileOutput { get; init; }

    /// <summary>
    /// The "From" email address used for all outgoing emails.
    /// </summary>
    public string FromAddress { get; init; } = "noreply@bankstatements.app";

    /// <summary>
    /// The display name used for the "From" address.
    /// </summary>
    public string FromName { get; init; } = "Bank Statements";

    /// <summary>
    /// SMTP server hostname.
    /// </summary>
    public string SmtpHost { get; init; } = "localhost";

    /// <summary>
    /// SMTP server port (587 for STARTTLS, 465 for SSL).
    /// </summary>
    public int SmtpPort { get; init; } = 587;

    /// <summary>
    /// SMTP username (if authentication is required).
    /// </summary>
    public string SmtpUsername { get; init; } = string.Empty;

    /// <summary>
    /// SMTP password (if authentication is required).
    /// </summary>
    public string SmtpPassword { get; init; } = string.Empty;
}
