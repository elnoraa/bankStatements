using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Statements.WebAPI.Services.Statements;

public sealed partial class PdfStatementParser : IStatementParser
{
    private readonly ILogger<PdfStatementParser> _logger;

    public PdfStatementParser(ILogger<PdfStatementParser> logger)
    {
        _logger = logger;
    }

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
            var lines = page.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _logger.LogTrace("Page {PageNumber}: {LineCount} lines", page.Number, lines.Length);

            foreach (var line in lines)
            {
                var transaction = TryParseLine(line);

                if (transaction is not null)
                {
                    transactions.Add(transaction);
                }
            }
        }

        _logger.LogInformation("Parsed {TransactionCount} transactions from {FilePath}", transactions.Count, filePath);
        return transactions;
    }

    private static ParsedStatementTransaction? TryParseLine(string line)
    {
        var dateMatch = DateAtStartRegex().Match(line);

        if (!dateMatch.Success || !TryParseDate(dateMatch.Groups["date"].Value, out var transactionDate))
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

        var transactionType = InferTransactionType(description, rawAmount);
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

    private static bool TryParseDate(string value, out DateOnly date)
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

    private static bool TryParseMoney(string value, out decimal amount)
    {
        var cleaned = value
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

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

    private static string InferTransactionType(string description, decimal amount)
    {
        if (amount < 0)
        {
            return "debit";
        }

        var normalized = description.ToLowerInvariant();
        var creditWords = new[] { "salary", "payroll", "wage", "refund", "interest", "deposit", "credit" };

        return creditWords.Any(normalized.Contains)
            ? "credit"
            : "debit";
    }

    private static string InferCategory(string description, string transactionType)
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

    [GeneratedRegex(@"^(?<date>\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2})\s+(?<rest>.+)$", RegexOptions.None, 1000)]
    private static partial Regex DateAtStartRegex();

    [GeneratedRegex(@"\(?-?\$?\d{1,3}(?:,\d{3})*(?:\.\d{2})\)?|\(?-?\$?\d+\.\d{2}\)?", RegexOptions.None, 1000)]
    private static partial Regex MoneyRegex();
}
