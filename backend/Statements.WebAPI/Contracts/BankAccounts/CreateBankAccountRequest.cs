using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.BankAccounts;

/// <summary>
/// Request to create a new bank account.
/// </summary>
/// <param name="AccountName">Optional display name for the account. Defaults to "Untitled" when null or empty.</param>
/// <param name="BankName">Optional institution name (e.g., "Commonwealth Bank").</param>
public sealed record CreateBankAccountRequest(
    [property: MaxLength(120)] string? AccountName,
    [property: MaxLength(120)] string? BankName);
