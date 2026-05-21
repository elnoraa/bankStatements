using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.BankAccounts;

/// <summary>
/// Request to create a new bank account.
/// </summary>
public sealed class CreateBankAccountRequest
{
    /// <summary>
    /// Optional display name for the account. Defaults to "Untitled" when null or empty.
    /// </summary>
    [MaxLength(120)]
    public string? AccountName { get; init; }

    /// <summary>
    /// Optional institution name (e.g., "Commonwealth Bank").
    /// </summary>
    [MaxLength(120)]
    public string? BankName { get; init; }
}
