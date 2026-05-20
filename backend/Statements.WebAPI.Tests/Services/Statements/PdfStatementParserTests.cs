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
}
