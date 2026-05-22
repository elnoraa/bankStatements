namespace Statements.WebAPI.Services.Audit;

/// <summary>
/// Provides audit logging for security-sensitive and data-modifying operations.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an auditable action to the audit log.
    /// </summary>
    /// <param name="userId">The user who performed the action (nullable for unauthenticated actions).</param>
    /// <param name="action">A short verb describing the action (e.g., "user.login", "statement.upload").</param>
    /// <param name="entityType">The type of entity affected (e.g., "statement", "user").</param>
    /// <param name="entityId">The ID of the affected entity, if applicable.</param>
    /// <param name="details">Optional JSON-serializable details about the action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(
        Guid? userId,
        string action,
        string? entityType,
        Guid? entityId,
        object? details,
        CancellationToken cancellationToken);
}
