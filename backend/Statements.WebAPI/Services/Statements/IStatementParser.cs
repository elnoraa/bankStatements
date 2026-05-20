namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Parses bank statement files to extract structured transaction data.
/// </summary>
public interface IStatementParser
{
    /// <summary>
    /// Parses a bank statement file and returns a list of extracted transactions.
    /// </summary>
    /// <param name="filePath">The absolute path to the statement file on disk.</param>
    /// <returns>A read-only list of parsed transactions.</returns>
    IReadOnlyList<ParsedStatementTransaction> Parse(string filePath);
}
