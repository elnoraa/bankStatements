using Dapper;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// CRUD service for user-defined savings goals.
/// </summary>
public sealed class SavingGoalService : ISavingGoalService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly ILogger<SavingGoalService> _logger;

    public SavingGoalService(IDbExecutor dbExecutor, ILogger<SavingGoalService> logger)
    {
        _dbExecutor = dbExecutor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavingGoalResponse>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        return (await _dbExecutor.QueryAsync<SavingGoalResponse>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id, name AS Name, target_amount AS TargetAmount,
                    current_amount AS CurrentAmount, currency AS Currency,
                    target_date AS TargetDate, is_completed AS IsCompleted,
                    CASE WHEN target_amount > 0
                        THEN LEAST((current_amount / target_amount) * 100, 100)
                        ELSE 0
                    END AS ProgressPercent
                FROM saving_goals
                WHERE user_id = @UserId
                ORDER BY created_at DESC
                """,
                new { UserId = userId },
                cancellationToken: cancellationToken)))
            .AsList();
    }

    /// <inheritdoc />
    public async Task<SavingGoalResponse> CreateAsync(Guid userId, CreateSavingGoalRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating saving goal for user {UserId}: {Name}", userId, request.Name);

        return await _dbExecutor.QuerySingleAsync<SavingGoalResponse>(
            new CommandDefinition(
                """
                INSERT INTO saving_goals (user_id, name, target_amount, currency, target_date)
                VALUES (@UserId, @Name, @TargetAmount, @Currency, @TargetDate)
                RETURNING
                    id AS Id, name AS Name, target_amount AS TargetAmount,
                    current_amount AS CurrentAmount, currency AS Currency,
                    target_date AS TargetDate, is_completed AS IsCompleted,
                    CASE WHEN target_amount > 0
                        THEN LEAST((current_amount / target_amount) * 100, 100)
                        ELSE 0
                    END AS ProgressPercent
                """,
                new
                {
                    UserId = userId,
                    request.Name,
                    request.TargetAmount,
                    request.Currency,
                    request.TargetDate
                },
                cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<SavingGoalResponse> UpdateAsync(Guid userId, Guid goalId, UpdateSavingGoalRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating saving goal {GoalId} for user {UserId}", goalId, userId);

        var result = await _dbExecutor.QuerySingleOrDefaultAsync<SavingGoalResponse>(
            new CommandDefinition(
                """
                UPDATE saving_goals
                SET
                    name = COALESCE(@Name, name),
                    target_amount = COALESCE(@TargetAmount, target_amount),
                    current_amount = COALESCE(@CurrentAmount, current_amount),
                    currency = COALESCE(@Currency, currency),
                    target_date = COALESCE(@TargetDate, target_date),
                    is_completed = CASE WHEN COALESCE(@CurrentAmount, current_amount) >= target_amount THEN TRUE ELSE is_completed END,
                    updated_at = NOW()
                WHERE id = @Id AND user_id = @UserId
                RETURNING
                    id AS Id, name AS Name, target_amount AS TargetAmount,
                    current_amount AS CurrentAmount, currency AS Currency,
                    target_date AS TargetDate, is_completed AS IsCompleted,
                    CASE WHEN target_amount > 0
                        THEN LEAST((current_amount / target_amount) * 100, 100)
                        ELSE 0
                    END AS ProgressPercent
                """,
                new
                {
                    Id = goalId,
                    UserId = userId,
                    request.Name,
                    request.TargetAmount,
                    request.CurrentAmount,
                    request.Currency,
                    request.TargetDate
                },
                cancellationToken: cancellationToken));

        if (result is null)
        {
            throw new InvalidOperationException("Saving goal not found.");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, Guid goalId, CancellationToken cancellationToken)
    {
        var rows = await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM saving_goals WHERE id = @Id AND user_id = @UserId",
                new { Id = goalId, UserId = userId },
                cancellationToken: cancellationToken));

        if (rows == 0)
        {
            throw new InvalidOperationException("Saving goal not found.");
        }

        _logger.LogInformation("Saving goal {GoalId} deleted by user {UserId}", goalId, userId);
    }
}
