using System.Text.Json;
using Dapper;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Audit;

/// <summary>
/// Writes audit log entries to the audit_log table via Dapper.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IDbExecutor dbExecutor, ILogger<AuditService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LogAsync(
        Guid? userId,
        string action,
        string? entityType,
        Guid? entityId,
        object? details,
        CancellationToken cancellationToken)
    {
        try
        {
            var detailsJson = details is not null
                ? JsonSerializer.Serialize(details)
                : null;

            await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO audit_log (user_id, action, entity_type, entity_id, details)
                    VALUES (@UserId, @Action, @EntityType, @EntityId, @Details::jsonb)
                    """,
                    new
                    {
                        UserId = userId,
                        Action = action,
                        EntityType = entityType,
                        EntityId = entityId,
                        Details = detailsJson ?? "{}"
                    },
                    cancellationToken: cancellationToken));

            _logger.LogDebug("Audit log entry created: {Action} by user {UserId}", action, userId);
        }
        catch (Exception ex)
        {
            // Audit logging should never break the calling operation
            _logger.LogError(ex, "Failed to write audit log entry: {Action} by user {UserId}", action, userId);
        }
    }
}
