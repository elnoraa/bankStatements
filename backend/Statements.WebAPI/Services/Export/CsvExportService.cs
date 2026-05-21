using System.Text;
using Dapper;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Export;

/// <summary>
/// Generates CSV from transaction data using StringBuilder to avoid external dependencies.
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    private readonly IDbExecutor _dbExecutor;

    public CsvExportService(IDbExecutor dbExecutor)
    {
        _dbExecutor = dbExecutor;
    }

    public async Task<byte[]> ExportTransactionsAsync(
        Guid userId,
        Guid? bankAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var transactions = await _dbExecutor.QueryAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT
                    t.transaction_date AS transactiondate,
                    t.description AS description,
                    COALESCE(c.name, 'Uncategorised') AS category,
                    t.amount AS amount,
                    t.transaction_type AS transactiontype
                FROM statement_transactions t
                JOIN bank_statements s ON s.id = t.bank_statement_id
                LEFT JOIN transaction_categories c ON c.id = t.category_id
                WHERE s.user_id = @UserId
                AND (@BankAccountId IS NULL OR t.bank_account_id = @BankAccountId)
                AND (@From::date IS NULL OR t.transaction_date >= @From::date)
                AND (@To::date IS NULL OR t.transaction_date <= @To::date)
                ORDER BY t.transaction_date DESC, t.created_at DESC
                """,
                new { UserId = userId, BankAccountId = bankAccountId, From = from, To = to },
                cancellationToken: cancellationToken));

        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Category,Amount,Type");

        foreach (var t in transactions)
        {
            var date = FormatDate(t.transactiondate);
            var desc = EscapeCsv(t.description as string ?? "");
            var cat = EscapeCsv(t.category as string ?? "");
            var amt = t.amount is decimal d ? d.ToString("F2") : "0.00";
            var type = t.transactiontype as string ?? "";

            sb.AppendLine($"{date},{desc},{cat},{amt},{type}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Formats a dynamic date value (DateOnly or DateTime) to yyyy-MM-dd string.
    /// Handles differences between Npgsql versions that return DateOnly vs DateTime.
    /// </summary>
    private static string FormatDate(object? value)
    {
        if (value is DateOnly d) return d.ToString("yyyy-MM-dd");
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd");
        return "";
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
