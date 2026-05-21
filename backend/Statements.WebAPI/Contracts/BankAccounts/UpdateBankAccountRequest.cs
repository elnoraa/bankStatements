using System.ComponentModel.DataAnnotations;

namespace Statements.WebAPI.Contracts.BankAccounts;

/// <summary>
/// Request to update an existing bank account's name or bank name.
/// </summary>
/// <param name="AccountName">The updated display name for the account.</param>
/// <param name="BankName">Optional updated institution name.</param>
public sealed record UpdateBankAccountRequest(
    [Required, MaxLength(120)] string AccountName,
    [MaxLength(120)] string? BankName);
