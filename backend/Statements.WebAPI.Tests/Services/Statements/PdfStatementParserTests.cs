using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Services.Statements;

/// <summary>
/// Unit tests for <see cref="PdfStatementParser"/>.
/// </summary>
public sealed class PdfStatementParserTests
{
    private readonly PdfStatementParser _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfStatementParserTests"/> class.
    /// </summary>
    public PdfStatementParserTests()
    {
        var logger = Mock.Of<ILogger<PdfStatementParser>>();
        _sut = new PdfStatementParser(logger);
    }

    /// <summary>
    /// Verifies that parsing a non-PDF file extension throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void Parse_WithNonPdfExtension_ThrowsInvalidOperationException()
    {
        var act = () => _sut.Parse("test.txt");

        act.Should().Throw<InvalidOperationException>().WithMessage("Only PDF bank statements are supported.");
    }

    /// <summary>
    /// Verifies that parsing a non-existent PDF file throws an exception.
    /// </summary>
    [Fact]
    public void Parse_WithNonExistentPdfFile_Throws()
    {
        var act = () => _sut.Parse("nonexistent.pdf");

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// Verifies that a DD/MM/YYYY date format is parsed correctly.
    /// </summary>
    [Fact]
    public void TryParseDate_WithDDMMYYYY_Succeeds()
    {
        var result = PdfStatementParser.TryParseDate("15/01/2025", out var date);

        result.Should().BeTrue();
        date.Should().Be(new DateOnly(2025, 1, 15));
    }

    /// <summary>
    /// Verifies that a D/Month/YYYY date format is parsed correctly.
    /// </summary>
    [Fact]
    public void TryParseDate_WithDMonthYYYY_Succeeds()
    {
        var result = PdfStatementParser.TryParseDate("5/1/2025", out var date);

        result.Should().BeTrue();
        date.Should().Be(new DateOnly(2025, 1, 5));
    }

    /// <summary>
    /// Verifies that a YYYY-MM-DD date format is parsed correctly.
    /// </summary>
    [Fact]
    public void TryParseDate_WithYYYYMMDD_Succeeds()
    {
        var result = PdfStatementParser.TryParseDate("2025-01-15", out var date);

        result.Should().BeTrue();
        date.Should().Be(new DateOnly(2025, 1, 15));
    }

    /// <summary>
    /// Verifies that an invalid date string returns false.
    /// </summary>
    [Fact]
    public void TryParseDate_WithInvalidString_ReturnsFalse()
    {
        var result = PdfStatementParser.TryParseDate("not-a-date", out _);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that various money formats (including negative in parentheses) are parsed correctly.
    /// </summary>
    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("$0.99", 0.99)]
    [InlineData("$1,000,000.00", 1000000.00)]
    [InlineData("($500.00)", -500.00)]
    [InlineData("($1,234.56)", -1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("(500.00)", -500.00)]
    public void TryParseMoney_WithVariousFormats_ReturnsCorrectAmount(string input, decimal expected)
    {
        var result = PdfStatementParser.TryParseMoney(input, out var amount);

        result.Should().BeTrue();
        amount.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that a non-monetary string returns false when parsing money.
    /// </summary>
    [Fact]
    public void TryParseMoney_WithInvalidString_ReturnsFalse()
    {
        var result = PdfStatementParser.TryParseMoney("abc", out _);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a line with date and amount produces a <see cref="ParsedStatementTransaction"/>.
    /// </summary>
    [Theory]
    [InlineData("15/01/2025 WITHDRAWAL $100.00", 100.00, "debit")]
    [InlineData("15/01/2025 SALARY $2000.00", 2000.00, "credit")]
    public void TryParseLine_WithDateAndAmount_ReturnsParsedTransaction(string line, decimal expectedAmount, string expectedType)
    {
        var result = PdfStatementParser.TryParseLine(line);

        result.Should().NotBeNull();
        result!.TransactionDate.Should().Be(new DateOnly(2025, 1, 15));
        result.Amount.Should().Be(expectedAmount);
        result.TransactionType.Should().Be(expectedType);
    }

    /// <summary>
    /// Verifies that a line without a date returns null.
    /// </summary>
    [Fact]
    public void TryParseLine_WithNoDate_ReturnsNull()
    {
        var result = PdfStatementParser.TryParseLine("No date here $100.00");

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a line without an amount returns null.
    /// </summary>
    [Fact]
    public void TryParseLine_WithNoAmount_ReturnsNull()
    {
        var result = PdfStatementParser.TryParseLine("15/01/2025 No amount here");

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that negative amounts in parentheses are parsed as negative values.
    /// </summary>
    [Fact]
    public void TryParseLine_WithNegativeAmountInParentheses_ReturnsNegativeAmount()
    {
        var result = PdfStatementParser.TryParseLine("15/01/2025 GROCERY STORE ($50.00)");

        result.Should().NotBeNull();
        result!.Amount.Should().Be(50.00m);
        result.TransactionType.Should().Be("debit");
    }

    /// <summary>
    /// Verifies that a line with amount and balance sets the BalanceAfter property.
    /// </summary>
    [Fact]
    public void TryParseLine_WithAmountAndBalance_ReturnsParsedTransactionWithBalanceAfter()
    {
        var result = PdfStatementParser.TryParseLine("15/01/2025 GROCERY STORE $50.00 $1,000.00");

        result.Should().NotBeNull();
        result!.Amount.Should().Be(50.00m);
        result.BalanceAfter.Should().Be(1000.00m);
    }

    /// <summary>
    /// Verifies that descriptions with credit keywords are classified as "credit".
    /// </summary>
    [Theory]
    [InlineData("salary", "credit")]
    [InlineData("payroll", "credit")]
    [InlineData("wage payment", "credit")]
    [InlineData("refund", "credit")]
    [InlineData("interest earned", "credit")]
    [InlineData("deposit", "credit")]
    [InlineData("credit", "credit")]
    public void InferTransactionType_WithCreditKeywords_ReturnsCredit(string description, string expected)
    {
        var result = PdfStatementParser.InferTransactionType(description, 100);

        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that debit transaction descriptions map to the correct spending categories.
    /// </summary>
    [Theory]
    [InlineData("coles", "Groceries")]
    [InlineData("WOOLWORTHS", "Groceries")]
    [InlineData("supermarket", "Groceries")]
    [InlineData("grocery store", "Groceries")]
    [InlineData("restaurant", "Dining")]
    [InlineData("cafe", "Dining")]
    [InlineData("dining out", "Dining")]
    [InlineData("train ticket", "Transport")]
    [InlineData("uber ride", "Transport")]
    [InlineData("transport NSW", "Transport")]
    [InlineData("opal card", "Transport")]
    [InlineData("rent payment", "Rent")]
    [InlineData("electric bill", "Utilities")]
    [InlineData("energy australia", "Utilities")]
    [InlineData("water bill", "Utilities")]
    [InlineData("utility bill", "Utilities")]
    [InlineData("bank fee", "Fees")]
    [InlineData("ATM fee", "Fees")]
    [InlineData("random purchase", "Uncategorised")]
    [InlineData("misc expense", "Uncategorised")]
    public void InferCategory_WithDebitTransactions_ReturnsCorrectCategory(string description, string expected)
    {
        var result = PdfStatementParser.InferCategory(description, "debit");

        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that credit transaction descriptions map to the correct income categories.
    /// </summary>
    [Theory]
    [InlineData("salary deposit", "Salary")]
    [InlineData("payroll credit", "Salary")]
    [InlineData("wage payment", "Salary")]
    [InlineData("refund received", "Refunds")]
    [InlineData("interest payment", "Interest")]
    [InlineData("bank transfer", "Transfers In")]
    [InlineData("payment received", "Transfers In")]
    public void InferCategory_WithCreditTransactions_ReturnsCorrectCategory(string description, string expected)
    {
        var result = PdfStatementParser.InferCategory(description, "credit");

        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that the description is correctly extracted from the transaction line.
    /// </summary>
    [Fact]
    public void TryParseLine_WithDateAndAmount_ExtractsDescription()
    {
        var result = PdfStatementParser.TryParseLine("15/01/2025 COLES SUPERMARKET $123.45");

        result.Should().NotBeNull();
        result!.Description.Should().Be("COLES SUPERMARKET");
        result.Amount.Should().Be(123.45m);
        result.TransactionType.Should().Be("debit");
    }

    /// <summary>
    /// Verifies that account/reference numbers containing dots (e.g. "260603.02.00002522")
    /// are not incorrectly matched as money amounts. Only the real dollar amount should be picked up.
    /// </summary>
    [Fact]
    public void TryParseLine_WithAccountNumberContainingDots_DoesNotMatchAccountNumber()
    {
        var line = "03 JUN TRANSFER FROM DGS DRAWINGS ACC 260603.02.00002522  $151.81";
        var result = PdfStatementParser.TryParseLine(line);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(151.81m);
        result.Description.Should().Contain("260603.02.00002522");
    }

    /// <summary>
    /// Verifies that a CR suffix on the amount forces credit type regardless of description.
    /// </summary>
    [Fact]
    public void TryParseLine_WithCRSuffix_SetsTransactionTypeToCredit()
    {
        var result = PdfStatementParser.TryParseLine("15/01/2025 PURCHASE $100.00 CR");

        result.Should().NotBeNull();
        result!.Amount.Should().Be(100.00m);
        result.TransactionType.Should().Be("credit");
    }

    /// <summary>
    /// Verifies that a DR suffix on the amount forces debit type regardless of description.
    /// </summary>
    [Fact]
    public void TryParseLine_WithDRSuffix_SetsTransactionTypeToDebit()
    {
        var result = PdfStatementParser.TryParseLine("15/01/2025 PURCHASE $50.00 DR");

        result.Should().NotBeNull();
        result!.Amount.Should().Be(50.00m);
        result.TransactionType.Should().Be("debit");
    }

    /// <summary>
    /// Verifies that short date format (DD MMM) with a complex description containing
    /// dotted reference numbers is parsed correctly — amount comes through, not the ref.
    /// </summary>
    [Fact]
    public void TryParseLine_WithShortDateFormatAndDottedReference_ParsesCorrectly()
    {
        var line = "03 JUN TRANSFER FROM DGS DRAWINGS ACC 260603.02.00002522  $151.81";
        var result = PdfStatementParser.TryParseLine(line);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(151.81m);
        result.Description.Should().Contain("260603.02.00002522");
    }

    /// <summary>
    /// Verifies that a line with multiple dot-separated numbers in the description
    /// (e.g. BPAY reference) still picks up the real dollar amount.
    /// </summary>
    [Fact]
    public void TryParseLine_WithMultipleDotsInDescription_DoesNotMatchNonMoneyValues()
    {
        var line = "15/01/2025 REF 123.456.789 BPAY BILLER 98765 $200.00";
        var result = PdfStatementParser.TryParseLine(line);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(200.00m);
    }

    /// <summary>
    /// Verifies that InferTransactionType returns "debit" for "transfer to" descriptions.
    /// </summary>
    [Fact]
    public void InferTransactionType_WithTransferTo_ReturnsDebit()
    {
        var result = PdfStatementParser.InferTransactionType("transfer to savings", 100);

        result.Should().Be("debit");
    }

    /// <summary>
    /// Verifies that InferTransactionType returns "credit" for "transfer from" descriptions
    /// (money coming in).
    /// </summary>
    [Fact]
    public void InferTransactionType_WithTransferFrom_ReturnsCredit()
    {
        var result = PdfStatementParser.InferTransactionType("transfer from savings", 100);

        result.Should().Be("credit");
    }

    /// <summary>
    /// Verifies that InferTransactionType returns "debit" for bare "transfer"
    /// (non-directional or ambiguous), since "transfer" has been removed from credit keywords.
    /// </summary>
    [Fact]
    public void InferTransactionType_WithBareTransfer_ReturnsDebit()
    {
        var result = PdfStatementParser.InferTransactionType("transfer", 100);

        result.Should().Be("debit");
    }

    // ── Column-based parsing (TryParseLineWithPositions) tests ──

    /// <summary>
    /// Helper: creates a <see cref="PdfStatementParser.PositionedWord"/> array with
    /// synthetic X positions spaced at 10px per character + 12px gap.
    /// </summary>
    private static PdfStatementParser.PositionedWord[] Positioned(params string[] texts)
    {
        double left = 0;
        return texts.Select(text =>
        {
            double width = text.Length * 10.0;
            var word = new PdfStatementParser.PositionedWord(text, left, left + width);
            left += width + 12.0;
            return word;
        }).ToArray();
    }

    /// <summary>
    /// Verifies that a standard debit line parses correctly via column-based parsing.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithStandardLine_ParsesCorrectly()
    {
        var words = Positioned("15/01/2025", "WITHDRAWAL", "$100.00");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.TransactionDate.Should().Be(new DateOnly(2025, 1, 15));
        result.Amount.Should().Be(100.00m);
        result.TransactionType.Should().Be("debit");
        result.Description.Should().Be("WITHDRAWAL");
    }

    /// <summary>
    /// Verifies that a credit line parses correctly via column-based parsing.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithCreditLine_ParsesCorrectly()
    {
        var words = Positioned("15/01/2025", "SALARY", "$2000.00");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.TransactionDate.Should().Be(new DateOnly(2025, 1, 15));
        result.Amount.Should().Be(2000.00m);
        result.TransactionType.Should().Be("credit");
        result.Description.Should().Be("SALARY");
    }

    /// <summary>
    /// Verifies that dotted account numbers in the description are not confused
    /// with money amounts — the amount is correctly extracted from the right.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithDottedAccountNumber_DoesNotMatchAccountNumber()
    {
        var words = Positioned("03", "JUN", "TRANSFER", "FROM", "DGS", "DRAWINGS",
            "ACC", "260603.02.00002522", "$151.81");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(151.81m);
        result.Description.Should().Contain("260603.02.00002522");
        // TRANSFER FROM → credit
        result.TransactionType.Should().Be("credit");
    }

    /// <summary>
    /// Verifies that dotted BPAY reference numbers in the description are
    /// not confused with money amounts.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithMultipleDotsInDescription_DoesNotMatchNonMoneyValues()
    {
        var words = Positioned("15/01/2025", "REF", "123.456.789", "BPAY", "BILLER", "98765", "$200.00");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(200.00m);
        result.Description.Should().Be("REF 123.456.789 BPAY BILLER 98765");
    }

    /// <summary>
    /// Verifies that a line with amount and balance parses both values.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithAmountAndBalance_ParsesBoth()
    {
        var words = Positioned("15/01/2025", "GROCERY", "STORE", "$50.00", "$1,000.00");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(50.00m);
        result.BalanceAfter.Should().Be(1000.00m);
    }

    /// <summary>
    /// Verifies that a CR suffix forces credit type.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithCRSuffix_SetsCredit()
    {
        var words = Positioned("15/01/2025", "PURCHASE", "$100.00", "CR");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(100.00m);
        result.TransactionType.Should().Be("credit");
    }

    /// <summary>
    /// Verifies that a DR suffix forces debit type.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithDRSuffix_SetsDebit()
    {
        var words = Positioned("15/01/2025", "PURCHASE", "$50.00", "DR");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(50.00m);
        result.TransactionType.Should().Be("debit");
    }

    /// <summary>
    /// Verifies that negative amounts in parentheses are handled correctly.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithNegativeAmount_ParsesNegativeAmount()
    {
        var words = Positioned("15/01/2025", "GROCERY", "STORE", "($50.00)");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(50.00m);
        result.TransactionType.Should().Be("debit");
    }

    /// <summary>
    /// Verifies that a row without a date returns null.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithoutDate_ReturnsNull()
    {
        var words = Positioned("No", "date", "here");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a row without a money amount returns null.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithoutMoney_ReturnsNull()
    {
        var words = Positioned("15/01/2025", "No", "money", "here");
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that an empty row returns null.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithEmptyInput_ReturnsNull()
    {
        var words = Array.Empty<PdfStatementParser.PositionedWord>();
        var result = PdfStatementParser.TryParseLineWithPositions(words);

        result.Should().BeNull();
    }

    // ── Column-based type inference tests ──

    /// <summary>
    /// Creates a <see cref="PdfStatementParser.PositionedWord"/> with given text
    /// and X position (sized to 10pt per character).
    /// </summary>
    private static PdfStatementParser.PositionedWord WordAt(string text, double left)
    {
        return new PdfStatementParser.PositionedWord(text, left, left + text.Length * 10.0);
    }

    /// <summary>
    /// Helper to build a column-roles dictionary for testing.
    /// </summary>
    private static Dictionary<PdfStatementParser.ColumnRole, double> Roles(
        params (PdfStatementParser.ColumnRole role, double x)[] entries)
    {
        return entries.ToDictionary(e => e.role, e => e.x);
    }

    /// <summary>
    /// Verifies that when column headers are detected and the amount is in the
    /// debit column (X≈400), the transaction is classified as "debit".
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithDebitColumn_ReturnsDebit()
    {
        var words = new[]
        {
            WordAt("15/01/2025", 50),
            WordAt("WITHDRAWAL", 150),
            WordAt("$200.00", 395),   // in debit column
            WordAt("$1,000.00", 595), // in balance column
        };
        var roles = Roles(
            (PdfStatementParser.ColumnRole.Debit, 400.0),
            (PdfStatementParser.ColumnRole.Credit, 500.0),
            (PdfStatementParser.ColumnRole.Balance, 600.0));
        var result = PdfStatementParser.TryParseLineWithPositions(words, roles);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(200.00m);
        result.TransactionType.Should().Be("debit");
        result.BalanceAfter.Should().Be(1000.00m);
    }

    /// <summary>
    /// Verifies that when column headers are detected and the amount is in the
    /// credit column (X≈500), the transaction is classified as "credit".
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithCreditColumn_ReturnsCredit()
    {
        var words = new[]
        {
            WordAt("15/01/2025", 50),
            WordAt("TRANSFER", 150),
            WordAt("$500.00", 495),   // in credit column
            WordAt("$1,500.00", 595), // in balance column
        };
        var roles = Roles(
            (PdfStatementParser.ColumnRole.Debit, 400.0),
            (PdfStatementParser.ColumnRole.Credit, 500.0),
            (PdfStatementParser.ColumnRole.Balance, 600.0));
        var result = PdfStatementParser.TryParseLineWithPositions(words, roles);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(500.00m);
        result.TransactionType.Should().Be("credit");
        result.BalanceAfter.Should().Be(1500.00m);
    }

    /// <summary>
    /// Verifies that when no column roles match (amount doesn't align with any
    /// detected header), the parser falls back to description-based inference.
    /// </summary>
    [Fact]
    public void TryParseLineWithPositions_WithNoMatchingRole_FallsBackToDescription()
    {
        // Amount at X=395, but only a balance column at X=600 is known — no match
        var words = new[]
        {
            WordAt("15/01/2025", 50),
            WordAt("SALARY", 150),
            WordAt("$2000.00", 395),
            WordAt("$5,000.00", 595),
        };
        var roles = Roles(
            (PdfStatementParser.ColumnRole.Balance, 600.0)); // only balance column known
        var result = PdfStatementParser.TryParseLineWithPositions(words, roles);

        // Falls back to description: "SALARY" contains "salary" → credit
        result.Should().NotBeNull();
        result!.TransactionType.Should().Be("credit");
    }

    /// <summary>
    /// Verifies that <see cref="PdfStatementParser.DetectColumnRoles"/>
    /// reads header words (Debit/Credit/Balance) from the first rows and
    /// maps them to their X positions.
    /// </summary>
    [Fact]
    public void DetectColumnRoles_WithHeaderRow_ReturnsRoleMapping()
    {
        var rows = new[]
        {
            // Header row with column names
            new[] { WordAt("Date", 50), WordAt("Description", 150),
                    WordAt("Debit", 380), WordAt("Credit", 490), WordAt("Balance", 590) },
            // Data rows
            new[] { WordAt("15/01/2025", 50), WordAt("desc", 150), WordAt("$50.00", 395), WordAt("$100.00", 595) },
        };

        var roles = PdfStatementParser.DetectColumnRoles(rows);

        roles.Should().HaveCount(3);
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Debit);
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Credit);
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Balance);
    }

    /// <summary>
    /// Verifies that <see cref="PdfStatementParser.DetectColumnRoles"/>
    /// recognizes synonym headers like "Withdrawals" and "Deposits".
    /// </summary>
    [Fact]
    public void DetectColumnRoles_WithSynonymHeaders_ReturnsRoleMapping()
    {
        var rows = new[]
        {
            new[] { WordAt("Date", 50), WordAt("Description", 150),
                    WordAt("Withdrawals", 380), WordAt("Deposits", 490), WordAt("Balance", 590) },
            new[] { WordAt("15/01/2025", 50), WordAt("desc", 150), WordAt("$50.00", 395), WordAt("$100.00", 595) },
        };

        var roles = PdfStatementParser.DetectColumnRoles(rows);

        roles.Should().HaveCount(3);
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Debit);   // "Withdrawals" → Debit
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Credit);   // "Deposits" → Credit
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Balance);
    }

    /// <summary>
    /// Verifies that <see cref="PdfStatementParser.DetectColumnRoles"/>
    /// falls back to data-based column detection when no header words are found,
    /// using the X positions of money values to infer columns.
    /// </summary>
    [Fact]
    public void DetectColumnRoles_WithNoHeaders_FallsBackToDataClustering()
    {
        var rows = new[]
        {
            // No header words — just data rows with money at X≈400 and X≈600
            new[] { WordAt("date", 50), WordAt("desc", 150), WordAt("$50.00", 395), WordAt("$100.00", 595) },
            new[] { WordAt("date", 50), WordAt("desc", 150), WordAt("$25.00", 400), WordAt("$75.00", 600) },
        };

        var roles = PdfStatementParser.DetectColumnRoles(rows);

        // Should detect 2 columns: Debit at ~400, Balance at ~600
        roles.Should().HaveCount(2);
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Debit);
        roles.Should().ContainKey(PdfStatementParser.ColumnRole.Balance);
        roles[PdfStatementParser.ColumnRole.Debit].Should().BeInRange(390, 410);
        roles[PdfStatementParser.ColumnRole.Balance].Should().BeInRange(590, 610);
    }
}
