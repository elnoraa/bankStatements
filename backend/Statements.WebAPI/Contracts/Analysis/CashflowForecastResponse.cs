namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A single month in a cash flow forecast.
/// </summary>
/// <param name="Month">The forecast month label (e.g., "2026-06").</param>
/// <param name="ProjectedCredit">Projected total credit for the month.</param>
/// <param name="ProjectedDebit">Projected total debit for the month.</param>
/// <param name="ProjectedBalance">Net projected balance for the month.</param>
/// <param name="Confidence">Confidence level: "high" when based on recurring data, "low" when based on averages alone.</param>
public sealed record ForecastMonth(
    string Month,
    decimal ProjectedCredit,
    decimal ProjectedDebit,
    decimal ProjectedBalance,
    string Confidence);

/// <summary>
/// Cash flow forecast projecting future months based on historical trends and recurring transactions.
/// </summary>
/// <param name="ForecastMonths">Ordered list of forecast months.</param>
/// <param name="TotalProjectedCredit">Sum of all projected credits across the forecast period.</param>
/// <param name="TotalProjectedDebit">Sum of all projected debits across the forecast period.</param>
/// <param name="AverageMonthlyCredit">Average monthly credit from the historical baseline.</param>
/// <param name="AverageMonthlyDebit">Average monthly debit from the historical baseline.</param>
public sealed record CashflowForecastResponse(
    IReadOnlyList<ForecastMonth> ForecastMonths,
    decimal TotalProjectedCredit,
    decimal TotalProjectedDebit,
    decimal AverageMonthlyCredit,
    decimal AverageMonthlyDebit);
