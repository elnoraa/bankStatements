namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// Spending analysis summary for a given period, including aggregated totals and breakdowns.
/// </summary>
/// <param name="PeriodStart">The start date of the analysis period (inclusive). Null if unbounded.</param>
/// <param name="PeriodEnd">The end date of the analysis period (inclusive). Null if unbounded.</param>
/// <param name="TotalCredit">Total amount of credit (income) transactions in the period.</param>
/// <param name="TotalDebit">Total amount of debit (expense) transactions in the period.</param>
/// <param name="NetCashflow">Net cash flow = total credit - total debit.</param>
/// <param name="IsCashflowPositive">Indicates whether the net cash flow is positive.</param>
/// <param name="SpendingByCategory">Breakdown of spending grouped by category.</param>
/// <param name="RecentTransactions">List of recent individual transactions.</param>
public sealed record SpendingSummaryResponse(
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal TotalCredit,
    decimal TotalDebit,
    decimal NetCashflow,
    bool IsCashflowPositive,
    IReadOnlyList<CategorySpendingResponse> SpendingByCategory,
    IReadOnlyList<RecentTransactionResponse> RecentTransactions);
