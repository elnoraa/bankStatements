namespace Statements.WebAPI.Contracts.BankAccounts;

/// <summary>
/// Response returned when listing, creating, or updating bank accounts.
/// </summary>
public sealed class BankAccountResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string BankName { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string? AccountMask { get; init; }
    public string Currency { get; init; } = "AUD";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
