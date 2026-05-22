using Statements.WebAPI.Contracts.Analysis;

namespace Statements.WebAPI.Services.Analysis;

/// <summary>
/// Manages user-defined savings goals.
/// </summary>
public interface ISavingGoalService
{
    /// <summary>Lists all saving goals for a user.</summary>
    Task<IReadOnlyList<SavingGoalResponse>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates a new saving goal.</summary>
    Task<SavingGoalResponse> CreateAsync(Guid userId, CreateSavingGoalRequest request, CancellationToken cancellationToken);

    /// <summary>Updates an existing saving goal. Returns the updated goal.</summary>
    Task<SavingGoalResponse> UpdateAsync(Guid userId, Guid goalId, UpdateSavingGoalRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a saving goal.</summary>
    Task DeleteAsync(Guid userId, Guid goalId, CancellationToken cancellationToken);
}
