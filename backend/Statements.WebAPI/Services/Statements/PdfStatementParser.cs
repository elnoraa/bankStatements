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

            // Group words by Y position (same table row), sort top-to-bottom, left-to-right.
            // Keep each word's positional data (Left/Right X) for column-based parsing.
            var lineWords = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(w => w.BoundingBox.Left)
                              .Select(w => new PositionedWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right))
                              .ToArray())
                .ToArray();

            // Read column headers (Debit / Credit / Withdrawals / Deposits / Balance) from the
            // first few rows to map X-positions to column roles for type inference.
            var columnRoles = DetectColumnRoles(lineWords);

            if (columnRoles.Count > 0)
            {
                var roleInfo = string.Join(", ",
                    columnRoles.Select(kv => $"{kv.Key}=X:{kv.Value:F0}"));
                _logger.LogDebug("Page {PageNumber}: column roles: [{Roles}]", page.Number, roleInfo);
            }

            transactions.AddRange(ParseLinesWithPositions(lineWords, columnRoles, _logger));
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

    // ── Column-based parsing (used by Parse for PDFs) ──────────

    /// <summary>
    /// Carries word text and its X-coordinate position within a PDF page row.
    /// </summary>
    internal sealed record PositionedWord(string Text, double Left, double Right);

    /// <summary>
    /// Identifies the role of a money column based on the PDF's column headers.
    /// </summary>
    internal enum ColumnRole { Debit, Credit, Balance }

    /// <summary>
    /// Parses an array of positioned-word rows into transactions using column-based logic.
    /// </summary>
    /// <param name="rows">Rows of positioned words, one per transaction line.</param>
    /// <param name="columnRoles">
    /// Mapping from column role to its X position, determined by reading PDF column headers.
    /// Pass null or empty to fall back to description-based type inference.
    /// </param>
    /// <param name="logger">Optional logger for debugging type-inference decisions.</param>
    internal static IReadOnlyList<ParsedStatementTransaction> ParseLinesWithPositions(
        PositionedWord[][] rows,
        IReadOnlyDictionary<ColumnRole, double>? columnRoles = null,
        ILogger? logger = null)
    {
        var transactions = new List<ParsedStatementTransaction>();
        var roles = columnRoles ?? new Dictionary<ColumnRole, double>();
        decimal? previousBalance = null;

        foreach (var words in rows)
        {
            var transaction = TryParseLineWithPositions(words, roles, logger, previousBalance);

            if (transaction is not null)
            {
                transactions.Add(transaction);

                if (transaction.BalanceAfter.HasValue)
                {
                    previousBalance = transaction.BalanceAfter.Value;
                }
            }
        }

        return transactions;
    }

    /// <summary>
    /// Attempts to parse a single row of positioned words using column boundaries.
    /// Date is extracted from the left side, money values from the right side,
    /// and description is everything in between.
    /// </summary>
    internal static ParsedStatementTransaction? TryParseLineWithPositions(
        PositionedWord[] words,
        IReadOnlyDictionary<ColumnRole, double>? columnRoles = null,
        ILogger? logger = null,
        decimal? previousBalance = null)
    {
        if (words.Length == 0)
        {
            return null;
        }

        // Step 1: Extract date from the leftmost words
        var (dateResult, dateWordCount) = ExtractDateFromLeft(words);
        if (dateResult is null)
        {
            return null;
        }

        var transactionDate = dateResult.Value;

        // Step 2: Extract money values from the rightmost words
        var (moneyValues, moneyWordCount) = ExtractMoneyFromRight(words, dateWordCount);

        if (moneyValues.Count == 0)
        {
            return null;
        }

        var amountText = moneyValues[0];
        string? balanceText = moneyValues.Count >= 2 ? moneyValues[^1] : null;

        if (!TryParseMoney(amountText, out var rawAmount))
        {
            return null;
        }

        decimal? balanceAfter = null;
        var balanceIsDr = false;

        if (balanceText is not null && TryParseMoney(balanceText, out var parsedBalance))
        {
            balanceAfter = parsedBalance;
            // Check if the balance has a "DR" suffix indicating a debit/negative balance
            // (overdrawn account). When DR, increasing balance = more debt, not more credit.
            balanceIsDr = balanceText.Trim().ToUpperInvariant().EndsWith("DR");
        }

        // Step 3: Extract description as the middle section (between date and money)
        var descEnd = words.Length - moneyWordCount;

        string description;

        if (descEnd > dateWordCount)
        {
            description = string.Join(" ",
                    words.Skip(dateWordCount).Take(descEnd - dateWordCount).Select(w => w.Text))
                .Trim(' ', '-', '|');
        }
        else
        {
            description = "Statement transaction";
        }

        // Step 4: Determine transaction type by matching amount's X-position
        //         against known column roles (Debit / Credit / Withdrawals / Deposits).
        //         Falls back to balance comparison if available, then description keywords.
        var rawSuffix = amountText.Trim().ToUpperInvariant();

        string transactionType;
        var typeSource = "unknown";

        if (rawSuffix.EndsWith("CR"))
        {
            transactionType = "credit";
            typeSource = "CR suffix on amount";
        }
        else
        {
            var amountLeft = words[words.Length - moneyWordCount].Left;
            (transactionType, typeSource) = InferTypeFromRoles(
                amountLeft, columnRoles, description, rawAmount, previousBalance, balanceAfter, balanceIsDr);
        }

        var amount = Math.Abs(rawAmount);

        var roleInfo = columnRoles is not null && columnRoles.Count > 0
            ? string.Join(", ", columnRoles.Select(kv => $"{kv.Key}=X:{kv.Value:F0}"))
            : "none";

        logger?.LogInformation(
            "[TypeInference] amount={RawAmount}, type={TransactionType}, " +
            "source=\"{TypeSource}\", desc=\"{Description}\", " +
            "amountLeft={AmountLeft:F1}, roles=[{Roles}], " +
            "prevBalance={PrevBalance}, currentBalance={CurrentBalance}",
            rawAmount, transactionType, typeSource, description,
            words.Length >= dateWordCount + moneyWordCount
                ? words[words.Length - moneyWordCount].Left
                : 0,
            roleInfo, previousBalance, balanceAfter);

        var categoryName = InferCategory(description, transactionType);

        return new ParsedStatementTransaction(
            transactionDate,
            description,
            amount,
            transactionType,
            balanceAfter,
            categoryName);
    }

    /// <summary>
    /// Reads column headers (Debit / Credit / Withdrawals / Deposits / Balance)
    /// from the first few rows of a page to map X-positions to roles.
    /// </summary>
    internal static Dictionary<ColumnRole, double> DetectColumnRoles(PositionedWord[][] rows)
    {
        var result = new Dictionary<ColumnRole, double>();

        // Known column header patterns (synonyms). The order here establishes
        // tie-breaking priority within a single row (debit checked first,
        // then credit, then balance).
        var debitHeaders = new[] { "debit", "debits", "withdrawal", "withdrawals", "withdrawl", "charge", "charges" };
        var creditHeaders = new[] { "credit", "credits", "deposit", "deposits" };
        var balanceHeaders = new[] { "balance", "running balance" };

        // Scan up to 10 rows to find column header words. The first page of a statement
        // often has a title/logo block that pushes the header row below row 3.
        foreach (var row in rows.Take(10))
        {
            foreach (var word in row)
            {
                var text = word.Text.Trim().ToLowerInvariant();

                if (!result.ContainsKey(ColumnRole.Debit) && debitHeaders.Contains(text))
                {
                    result[ColumnRole.Debit] = (word.Left + word.Right) / 2;
                }
                else if (!result.ContainsKey(ColumnRole.Credit) && creditHeaders.Contains(text))
                {
                    result[ColumnRole.Credit] = (word.Left + word.Right) / 2;
                }
                else if (!result.ContainsKey(ColumnRole.Balance) && balanceHeaders.Contains(text))
                {
                    result[ColumnRole.Balance] = (word.Left + word.Right) / 2;
                }
            }
        }

        // ── Validate header positions ──
        // Header words like "Debit" sometimes appear in non-header rows (summary
        // rows, instruction text) at positions that don't reflect the actual
        // money columns. If any two detected headers are within 30px, they are
        // likely false positives — discard ALL header results and fall back to
        // data-based column detection.
        if (result.Count >= 2)
        {
            var sorted = result.Values.OrderBy(x => x).ToList();
            var tooClose = false;

            for (var i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - sorted[i - 1] < 30.0)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
            {
                result.Clear();
            }
        }

        // ── Data-based fallback ──
        // If header detection failed (no results discarded, or not enough found),
        // cluster money-word X positions from data rows and assign roles by order.
        if (result.Count < 2)
        {
            var headerWordSet = debitHeaders.Concat(creditHeaders).Concat(balanceHeaders).ToHashSet();
            var dataX = rows
                .SkipWhile(r => r.Any(w => headerWordSet.Contains(w.Text.Trim().ToLowerInvariant())))
                .SelectMany(r => r)
                .Where(w => IsPlausibleMoneyValue(w.Text))
                .Select(w => w.Left)
                .Order()
                .ToList();

            if (dataX.Count > 0)
            {
                // Cluster by X-gap
                const double gapThreshold = 30.0;
                var clusters = new List<List<double>> { new() { dataX[0] } };

                foreach (var x in dataX.Skip(1))
                {
                    if (x - clusters[^1][^1] > gapThreshold)
                        clusters.Add(new List<double>());
                    clusters[^1].Add(x);
                }

                var clusterMedians = clusters.Select(c => c[c.Count / 2]).ToArray();

                // Rightmost cluster = Balance
                if (clusters.Count >= 2)
                {
                    result[ColumnRole.Balance] = clusterMedians[^1];

                    // Leftmost = Debit | next (if any) = Credit
                    result[ColumnRole.Debit] = clusterMedians[0];

                    if (clusters.Count >= 3)
                    {
                        result[ColumnRole.Credit] = clusterMedians[1];
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Determines whether an amount is debit or credit. First tries matching the
    /// amount's X-position against known column roles (Debit/Credit column headers).
    /// If that doesn't work and we have balance data, uses the running balance to
    /// infer: balance going down → debit, balance going up → credit.
    /// Falls back to description keywords as a last resort.
    /// </summary>
    internal static (string type, string source) InferTypeFromRoles(
        double amountLeft,
        IReadOnlyDictionary<ColumnRole, double>? columnRoles,
        string description,
        decimal rawAmount,
        decimal? previousBalance = null,
        decimal? currentBalance = null,
        bool balanceIsDr = false)
    {
        const double columnProximity = 30.0; // max X-distance to consider a match

        if (columnRoles is not null)
        {
            if (columnRoles.TryGetValue(ColumnRole.Debit, out var debitX) &&
                Math.Abs(amountLeft - debitX) <= columnProximity)
            {
                return ("debit", $"column header: X={amountLeft:F1} matches debit column at X={debitX:F1}");
            }

            if (columnRoles.TryGetValue(ColumnRole.Credit, out var creditX) &&
                Math.Abs(amountLeft - creditX) <= columnProximity)
            {
                return ("credit", $"column header: X={amountLeft:F1} matches credit column at X={creditX:F1}");
            }
        }

        // If no column header matched and we have balance data, use running-balance
        // comparison. When the balance has a "DR" suffix (account overdrawn / in debit),
        // the direction is inverted: increasing balance means more debt, not more credit.
        if (previousBalance.HasValue && currentBalance.HasValue)
        {
            var absAmount = Math.Abs(rawAmount);

            if (balanceIsDr)
            {
                // DR balance (debit/negative): increasing balance = more debit
                if (Math.Abs(currentBalance.Value - (previousBalance.Value + absAmount)) < 0.01m)
                {
                    return ("debit",
                        $"balance check (DR): {previousBalance:F2} + {absAmount:F2} = {currentBalance:F2}");
                }

                if (Math.Abs(currentBalance.Value - (previousBalance.Value - absAmount)) < 0.01m)
                {
                    return ("credit",
                        $"balance check (DR): {previousBalance:F2} - {absAmount:F2} = {currentBalance:F2}");
                }
            }
            else
            {
                // Normal (positive) balance: decreasing = debit, increasing = credit
                if (Math.Abs(currentBalance.Value - (previousBalance.Value - absAmount)) < 0.01m)
                {
                    return ("debit",
                        $"balance check: {previousBalance:F2} - {absAmount:F2} = {currentBalance:F2}");
                }

                if (Math.Abs(currentBalance.Value - (previousBalance.Value + absAmount)) < 0.01m)
                {
                    return ("credit",
                        $"balance check: {previousBalance:F2} + {absAmount:F2} = {currentBalance:F2}");
                }
            }
        }

        // Last resort: description keywords
        var fallbackType = InferTransactionType(description, rawAmount);
        var roleCount = columnRoles?.Count ?? 0;

        return (fallbackType,
            $"description (X={amountLeft:F1} didn't match any of {roleCount} roles" +
            (previousBalance.HasValue ? ", balance check didn't match either)" : ")"));
    }
    /// <summary>
    /// Tries the leftmost 1-3 words as a date. Tries longer spans first so that
    /// "13 Feb 26" is consumed as 3 words (d MMM yy) rather than stopping at "13 Feb".
    /// Returns the parsed date and how many words were consumed.
    /// </summary>
    private static (DateOnly? date, int wordCount) ExtractDateFromLeft(PositionedWord[] words)
    {
        var maxDateWords = Math.Min(3, words.Length);

        // Try 3 → 2 → 1 so that "13 Feb 26" is consumed entirely rather than
        // stopping at "13 Feb" and leaving "26" (year 2026) in the description.
        for (var i = maxDateWords; i >= 1; i--)
        {
            var candidate = string.Join(" ", words.Take(i).Select(w => w.Text));

            if (TryParseDate(candidate, out var date))
            {
                return (date, i);
            }

            if (TryParseShortDate(candidate, out date))
            {
                return (date, i);
            }
        }

        return (null, 0);
    }

    /// <summary>
    /// Scans right-to-left from the end of the row, collecting consecutive
    /// money-like values. Returns them in left-to-right order and the total
    /// number of words consumed.
    /// </summary>
    private static (List<string> moneyValues, int moneyWordCount) ExtractMoneyFromRight(
        PositionedWord[] words, int dateWordCount)
    {
        var moneyValues = new List<string>();
        var scanIdx = words.Length - 1;

        while (scanIdx >= dateWordCount)
        {
            var found = false;

            // Try longer spans first (greedy from right) — 3 words, then 2, then 1
            for (var len = 3; len >= 1; len--)
            {
                var startIdx = scanIdx - len + 1;

                if (startIdx < dateWordCount)
                {
                    continue;
                }

                var candidate = string.Join(" ",
                    words.Skip(startIdx).Take(len).Select(w => w.Text));

                if (IsPlausibleMoneyValue(candidate) && TryParseMoney(candidate, out _))
                {
                    moneyValues.Add(candidate);
                    scanIdx -= len;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                break;
            }
        }

        // Reverse to restore left-to-right order
        moneyValues.Reverse();

        var moneyWordCount = words.Length - 1 - scanIdx;
        return (moneyValues, moneyWordCount);
    }

    /// <summary>
    /// Quick pre-check: a plausible money value must contain a '$' sign
    /// OR end with exactly two decimal digits (".XX"). This prevents dotted
    /// account/reference numbers like "260603.02.00002522" from being treated
    /// as money amounts.
    /// </summary>
    private static bool IsPlausibleMoneyValue(string text)
    {
        var trimmed = text.Trim().ToUpperInvariant();

        if (trimmed.EndsWith("CR") || trimmed.EndsWith("DR"))
        {
            trimmed = trimmed[..^2].TrimEnd();
        }

        return trimmed.Contains('$') ||
               Regex.IsMatch(trimmed, @"\.\d{2}$");
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
        // try current year first; if the result is after today, use previous year.
        // Also handle "DD MMM YY" (e.g. "13 Feb 26") where the 2-digit year is included.
        var formats4 = new[] { "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy" };
        var formats2 = new[] { "d MMM yy", "dd MMM yy", "d MMMM yy", "dd MMMM yy" };

        // First try parsing the full value with 2-digit year formats (e.g. "13 Feb 26")
        if (DateOnly.TryParseExact(value, formats2, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            // C# parses "26" → 2026 (based on two-digit year cutoff). If the result
            // is in the future, try the previous century as well.
            if (date > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                date = date.AddYears(-100);
            }
            return true;
        }

        // Fall back to appending current/previous year for "DD MMM" (without year)
        var thisYear = DateTime.UtcNow.Year;

        if (DateOnly.TryParseExact($"{value} {thisYear}", formats4, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            if (date > DateOnly.FromDateTime(DateTime.UtcNow))
            {
                date = date.AddYears(-1);
            }
            return true;
        }

        if (DateOnly.TryParseExact($"{value} {thisYear - 1}", formats4, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
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

        var creditWords = new[] { "salary", "payroll", "wage", "refund", "interest", "deposit", "credit" };

        // Handle "payment" contextually:
        //   "PAYMENT FROM X" = money coming in (credit)
        //   "PAYMENT TO X" = sending money out (debit)
        if (normalized.Contains("payment from"))
        {
            return "credit";
        }
        if (normalized.Contains("payment to"))
        {
            return "debit";
        }

        // Handle transfers contextually:
        //   "TRANSFER TO X" or "TRANSFER-OUT" = sending money out (debit)
        //   "TRANSFER FROM X" = money coming in (credit)
        //   Bare/ambiguous "transfer" defaults to debit per this bank's convention.
        if (normalized.Contains("transfer from"))
        {
            return "credit";
        }
        if (normalized.Contains("transfer to") || normalized.Contains("transfer-out"))
        {
            return "debit";
        }

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

        // Order matters: check more specific categories before generic ones
        if (normalized.Contains("insurance") || normalized.Contains("aami") || normalized.Contains("nib ") || normalized.Contains("medibank"))
        {
            return "Insurance";
        }

        if (normalized.Contains("rent"))
        {
            return "Rent";
        }

        if (normalized.Contains("electric") || normalized.Contains("energy") || normalized.Contains("water") || normalized.Contains("utility") ||
            normalized.Contains("phone") || normalized.Contains("mobile") || normalized.Contains("internet") || normalized.Contains("broadband") ||
            normalized.Contains("telstra") || normalized.Contains("optus"))
        {
            return "Utilities";
        }

        if (normalized.Contains("grocery") || normalized.Contains("supermarket") || normalized.Contains("coles") || normalized.Contains("woolworths") ||
            normalized.Contains("aldi") || normalized.Contains("iga ") || normalized.Contains("foodland") || normalized.Contains("food ") ||
            normalized.Contains("butcher") || normalized.Contains("bakery"))
        {
            return "Groceries";
        }

        if (normalized.Contains("restaurant") || normalized.Contains("cafe") || normalized.Contains("dining") || normalized.Contains("takeaway") ||
            normalized.Contains("mcdonald") || normalized.Contains("maccas") || normalized.Contains("kfc") || normalized.Contains("hungry jack") ||
            normalized.Contains("pizza") || normalized.Contains("subway") || normalized.Contains("starbucks"))
        {
            return "Dining";
        }

        if (normalized.Contains("train") || normalized.Contains("transport") || normalized.Contains("uber") || normalized.Contains("opal") ||
            normalized.Contains("toll") || normalized.Contains("petrol") || normalized.Contains("fuel") || normalized.Contains("service station") ||
            normalized.Contains("parking"))
        {
            return "Transport";
        }

        if (normalized.Contains("doctor") || normalized.Contains("dentist") || normalized.Contains("medical") || normalized.Contains("hospital") ||
            normalized.Contains("pharmacy") || normalized.Contains("chemist"))
        {
            return "Health";
        }

        if (normalized.Contains("amazon") || normalized.Contains("ebay") || normalized.Contains("target") || normalized.Contains("kmart") ||
            normalized.Contains("big w") || normalized.Contains("bunnings") || normalized.Contains("shop ") || normalized.Contains("store"))
        {
            return "Shopping";
        }

        if (normalized.Contains("netflix") || normalized.Contains("disney") || normalized.Contains("spotify") || normalized.Contains("apple ") ||
            normalized.Contains("subscription") || normalized.Contains("streaming"))
        {
            return "Entertainment";
        }

        if (normalized.Contains("gym") || normalized.Contains("fitness") || normalized.Contains("sport") ||
            normalized.Contains("exercise") || normalized.Contains("nike") || normalized.Contains("adidas") ||
            normalized.Contains("rebel"))
        {
            return "Sport";
        }

        if (normalized.Contains("fee") || normalized.Contains("atm ") || normalized.Contains("withdrawal"))
        {
            return "Fees";
        }

        return "Uncategorised";
    }

    [GeneratedRegex(@"^(?<date>\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}[/-]\d{1,2}[/-]\d{1,2}|\d{1,2}\s+(?:JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC))\s+(?<rest>.+)$", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex DateAtStartRegex();

    [GeneratedRegex(@"\(?-?\$?\d{1,3}(?:,\d{3})*(?:\.\d{2})\)?(?!\.?\d)\s*(?:CR|DR)?|\(?-?\$?\d+\.\d{2}\)?(?!\.?\d)\s*(?:CR|DR)?", RegexOptions.None, 1000)]
    private static partial Regex MoneyRegex();
}
