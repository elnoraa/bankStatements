using System.Dynamic;
using Dapper;
using FluentAssertions;
using Moq;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Export;

namespace Statements.WebAPI.Tests.Services.Export;

/// <summary>
/// Unit tests for <see cref="CsvExportService"/>.
/// </summary>
public sealed class CsvExportServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly CsvExportService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public CsvExportServiceTests()
    {
        _sut = new CsvExportService(_dbExecutorMock.Object);
    }

    /// <summary>
    /// Creates an ExpandoObject for mocking Dapper's dynamic query results.
    /// </summary>
    private static dynamic CreateDynamicRow(params (string Key, object Value)[] properties)
    {
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;
        foreach (var (key, value) in properties)
        {
            dict[key] = value;
        }
        return expando;
    }

    [Fact]
    public async Task ExportTransactionsAsync_ReturnsCsvWithHeaders()
    {
        _dbExecutorMock
            .Setup(x => x.QueryAsync<dynamic>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<dynamic>());

        var result = await _sut.ExportTransactionsAsync(_userId, null, null, null, CancellationToken.None);

        var csv = System.Text.Encoding.UTF8.GetString(result);
        csv.Should().StartWith("Date,Description,Category,Amount,Type");
    }

    [Fact]
    public async Task ExportTransactionsAsync_IncludesTransactionRows()
    {
        var row = CreateDynamicRow(
            ("transactiondate", new DateOnly(2026, 1, 15)),
            ("description", "Coles"),
            ("category", "Groceries"),
            ("amount", 50.00m),
            ("transactiontype", "debit"));

        _dbExecutorMock
            .Setup(x => x.QueryAsync<dynamic>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<dynamic> { row });

        var result = await _sut.ExportTransactionsAsync(_userId, null, null, null, CancellationToken.None);

        var csv = System.Text.Encoding.UTF8.GetString(result);
        csv.Should().Contain("2026-01-15");
        csv.Should().Contain("Coles");
        csv.Should().Contain("Groceries");
        csv.Should().Contain("50.00");
        csv.Should().Contain("debit");
    }
}
