using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.BankAccounts;

/// <summary>
/// Request to update an existing bank account's name or bank name.
/// </summary>
public sealed class UpdateBankAccountRequest
{
    /// <summary>
    /// The updated display name for the account.
    /// </summary>
    [Required, MaxLength(120)]
    public string AccountName { get; init; } = null!;

    /// <summary>
    /// Optional updated institution name.
    /// </summary>
    [MaxLength(120)]
    public string? BankName { get; init; }
}
