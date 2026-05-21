namespace Statements.WebAPI.Services.Export;

/// <summary>
/// Exports transaction data to CSV format.
/// </summary>
public interface ICsvExportService
{
    /// <summary>
    /// Exports transactions for the given user and optional filters as a CSV byte array.
    /// </summary>
    /// <param name="userId">The owning user.</param>
    /// <param name="bankAccountId">Optional bank account filter.</param>
    /// <param name="from">Optional start date filter.</param>
    /// <param name="to">Optional end date filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>UTF-8 encoded CSV bytes.</returns>
    Task<byte[]> ExportTransactionsAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}
