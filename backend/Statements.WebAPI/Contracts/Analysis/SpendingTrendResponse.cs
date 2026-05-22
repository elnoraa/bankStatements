namespace Statements.WebAPI.Contracts.Analysis;

/// <summary>
/// A single data point in a spending trend series.
/// </summary>
/// <param name="Period">The period label (e.g., "2026-01", "2026-Q1").</param>
/// <param name="Category">The spending category name.</param>
/// <param name="Total">The total amount spent in this period for this category.</param>
public sealed record SpendingTrendPoint(
    string Period,
    string Category,
    decimal Total);

/// <summary>
/// Spending trends aggregated by period (monthly, quarterly, yearly).
/// </summary>
/// <param name="Periods">Ordered list of period labels.</param>
/// <param name="Categories">Distinct category names included in the trends.</param>
/// <param name="DataPoints">Individual data points for each period and category.</param>
public sealed record SpendingTrendResponse(
    IReadOnlyList<string> Periods,
    IReadOnlyList<string> Categories,
    IReadOnlyList<SpendingTrendPoint> DataPoints);
