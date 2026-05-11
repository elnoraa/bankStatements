namespace Statements.WebAPI.Contracts.Analysis;

public sealed record SpendingSummaryResponse(
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal TotalCredit,
    decimal TotalDebit,
    decimal NetCashflow,
    bool IsCashflowPositive,
    IReadOnlyList<CategorySpendingResponse> SpendingByCategory,
    IReadOnlyList<RecentTransactionResponse> RecentTransactions);
