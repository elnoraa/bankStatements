using Statements.WebAPI.Contracts.Analysis;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Manages monthly budgets per category.
/// </summary>
public interface IBudgetService
{
    /// <summary>Lists budgets for a user in a given month.</summary>
    Task<IReadOnlyList<BudgetResponse>> ListAsync(Guid userId, DateOnly monthYear, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a budget (upserts on user_id + category_id + month_year).
    /// </summary>
    Task<BudgetResponse> CreateOrUpdateAsync(Guid userId, CreateBudgetRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a budget by ID (verifies ownership).</summary>
    Task DeleteAsync(Guid userId, Guid budgetId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns budget vs. actual spending for each budgeted category in a given month.
    /// </summary>
    Task<IReadOnlyList<BudgetProgressResponse>> GetProgressAsync(Guid userId, DateOnly monthYear, Guid? bankAccountId, CancellationToken cancellationToken);
}
