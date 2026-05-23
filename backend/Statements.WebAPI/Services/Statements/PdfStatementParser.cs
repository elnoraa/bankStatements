using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Parses PDF bank statement files to extract structured transaction data using the PdfPig library.
/// </summary>
public sealed partial class PdfStatementParser : IStatementParser
{
    private readonly ILogger<PdfStatementParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStatementParser"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PdfStatementParser(ILogger<PdfStatementParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ParsedStatementTransaction> Parse(string filePath)
    {
        _logger.LogInformation("Parsing PDF statement: {FilePath}", filePath);

        if (!Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Parse rejected - not a PDF file: {FilePath}", filePath);
            throw new InvalidOperationException("Only PDF bank statements are supported.");
        }

        using var document = PdfDocument.Open(filePath);
        var transactions = new List<ParsedStatementTransaction>();
        var pageCount = document.NumberOfPages;

        _logger.LogDebug("PDF has {PageCount} pages", pageCount);

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();

            if (words.Count == 0)
            {
                continue;
            }

            // Group words by Y position (same table row), sort top-to-bottom, left-to-right
            var lines = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
                .ToArray();

            transactions.AddRange(ParseLines(lines));
        }

        _logger.LogInformation("Parsed {TransactionCount} transactions from {FilePath}", transactions.Count, filePath);
        return transactions;
    }

    /// <inheritdoc />
    public IReadOnlyList<ParsedStatementTransaction> ParseText(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var transactions = ParseLines(lines);
        _logger.LogInformation("Parsed {TransactionCount} transactions from text ({LineCount} lines)", transactions.Count, lines.Length);
        return transactions;
    }

    internal static IReadOnlyList<ParsedStatementTransaction> ParseLines(string[] lines)
    {
        var transactions = new List<ParsedStatementTransaction>();

        foreach (var line in lines)
        {
            var transaction = TryParseLine(line);

            if (transaction is not null)
            {
                transactions.Add(transaction);
            }
        }

        return transactions;
    }

    /// <summary>
    /// Attempts to parse a single transaction line from the PDF text content.
    /// </summary>
    /// <param name="line">A line of text extracted from the PDF.</param>
    /// <returns>A <see cref="ParsedStatementTransaction"/> if the line matches the expected format; otherwise <c>null</c>.</returns>
    internal static ParsedStatementTransaction? TryParseLine(string line)
    {
        var dateMatch = DateAtStartRegex().Match(line);

        if (!dateMatch.Success)
        {
            return null;
        }

        var dateValue = dateMatch.Groups["date"].Value;
        if (!TryParseDate(dateValue, out var transactionDate) && !TryParseShortDate(dateValue, out transactionDate))
        {
            return null;
        }

        var rest = dateMatch.Groups["rest"].Value.Trim();
        var amountMatches = MoneyRegex().Matches(rest);

        if (amountMatches.Count == 0)
        {
            return null;
        }

        var amountMatch = amountMatches.Count >= 2
            ? amountMatches[^2]
            : amountMatches[^1];

        if (!TryParseMoney(amountMatch.Value, out var rawAmount))
        {
            return null;
        }

        decimal? balanceAfter = null;

        if (amountMatches.Count >= 2 && TryParseMoney(amountMatches[^1].Value, out var parsedBalance))
        {
            balanceAfter = parsedBalance;
        }

        var description = rest[..amountMatch.Index].Trim(' ', '-', '|');

        if (string.IsNullOrWhiteSpace(description))
        {
            description = "Statement transaction";
        }

        // CR/DR suffix overrides type inference
        var rawSuffix = amountMatch.Value.Trim().ToUpperInvariant();
        var transactionType = rawSuffix.EndsWith("CR")
            ? "credit"
            : InferTransactionType(description, rawAmount);
        var amount = Math.Abs(rawAmount);
        var categoryName = InferCategory(description, transactionType);

        return new ParsedStatementTransaction(
            transactionDate,
            description,
            amount,
            transactionType,
            balanceAfter,
            categoryName);
    }

    internal static bool TryParseDate(string value, out DateOnly date)
    {
        var formats = new[]
        {
            "d/M/yyyy",
            "dd/MM/yyyy",
            "d-M-yyyy",
            "dd-MM-yyyy",
            "yyyy-M-d",
            "yyyy-MM-dd",
            "d/M/yy",
            "dd/MM/yy",
            "d-M-yy",
            "dd-MM-yy"
        };

        return DateOnly.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    internal static bool TryParseShortDate(string value, out DateOnly date)
    {
        // Handle "DD MMM" format (e.g. "10 DEC") — infer the year:
        // try current year first; if the result is after today, use previous year
        var formats = new[] { "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy" };
        var thisYear = DateTime.UtcNow.Year;

        if (DateOnly.TryParseExact($"{value} {thisYear}", formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            if (date > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                date = date.AddYears(-1);
            }
            return true;
        }

        if (DateOnly.TryParseExact($"{value} {thisYear - 1}", formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        date = default;
        return false;
    }

    internal static bool TryParseMoney(string value, out decimal amount)
    {
        var cleaned = value
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

        // Strip CR/DR suffix before parsing the number
        if (cleaned.EndsWith("CR") || cleaned.EndsWith("DR"))
        {
            cleaned = cleaned[..^2].TrimEnd();
        }

        var isNegative = cleaned.StartsWith('(') && cleaned.EndsWith(')');
        cleaned = cleaned.Trim('(', ')');

        if (!decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (isNegative)
        {
            amount *= -1;
        }

        return true;
    }

    internal static string InferTransactionType(string description, decimal amount)
    {
        if (amount < 0)
        {
            return "debit";
        }

        var normalized = description.ToLowerInvariant();
        var creditWords = new[] { "salary", "payroll", "wage", "refund", "interest", "deposit", "credit", "transfer" };

        return creditWords.Any(normalized.Contains)
            ? "credit"
            : "debit";
    }

    internal static string InferCategory(string description, string transactionType)
    {
        var normalized = description.ToLowerInvariant();

        if (transactionType == "credit")
        {
            if (normalized.Contains("salary") || normalized.Contains("payroll") || normalized.Contains("wage"))
            {
                return "Salary";
            }

            if (normalized.Contains("refund"))
            {
                return "Refunds";
            }

            if (normalized.Contains("interest"))
            {
                return "Interest";
            }

            return "Transfers In";
        }

        if (normalized.Contains("grocery") || normalized.Contains("supermarket") || normalized.Contains("coles") || normalized.Contains("woolworths"))
        {
            return "Groceries";
        }

        if (normalized.Contains("restaurant") || normalized.Contains("cafe") || normalized.Contains("dining"))
        {
            return "Dining";
        }

        if (normalized.Contains("train") || normalized.Contains("transport") || normalized.Contains("uber") || normalized.Contains("opal"))
        {
            return "Transport";
        }

        if (normalized.Contains("rent"))
        {
            return "Rent";
        }

        if (normalized.Contains("electric") || normalized.Contains("energy") || normalized.Contains("water") || normalized.Contains("utility"))
        {
            return "Utilities";
        }

        if (normalized.Contains("fee"))
        {
            return "Fees";
        }

        return "Uncategorised";
    }

    [GeneratedRegex(@"^(?<date>\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|\d{1,2}\s+(?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC))\s+(?<rest>.+)$", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex DateAtStartRegex();

    [GeneratedRegex(@"\(?-?\$?\d{1,3}(?:,\d{3})*(?:\.\d{2})\)?\s*(?:CR|DR)?|\(?-?\$?\d+\.\d{2}\)?\s*(?:CR|DR)?", RegexOptions.None, 1000)]
    private static partial Regex MoneyRegex();
}
