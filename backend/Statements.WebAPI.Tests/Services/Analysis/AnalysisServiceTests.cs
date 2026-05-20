using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Tests.Services.Analysis;

/// <summary>
/// Unit tests for <see cref="AnalysisService"/>.
/// </summary>
public sealed class AnalysisServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly AnalysisService _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisServiceTests"/> class.
    /// </summary>
    public AnalysisServiceTests()
    {
        _sut = new AnalysisService(_dbExecutorMock.Object, Mock.Of<ILogger<AnalysisService>>());
    }

    /// <summary>
    /// Verifies that a valid user with transactions returns a complete spending summary.
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_WithValidUser_ReturnsSpendingSummary()
    {
        var userId = Guid.NewGuid();

        var totals = new AnalysisService.CashflowTotals
        {
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            TotalCredit = 50000m,
            TotalDebit = 35000m
        };

        var spendingByCategory = new List<CategorySpendingResponse>
        {
            new() { Category = "Groceries", TotalDebit = 5000m, TransactionCount = 50 },
            new() { Category = "Rent", TotalDebit = 24000m, TransactionCount = 12 },
        };

        var recentTransactions = new List<RecentTransactionResponse>
        {
            new() { Id = Guid.NewGuid(), TransactionDate = new DateOnly(2025, 12, 15), Description = "Coles", Amount = 150m, TransactionType = "debit", Category = "Groceries" },
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(totals);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<CategorySpendingResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(spendingByCategory);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<RecentTransactionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(recentTransactions);

        var result = await _sut.GetSummaryAsync(userId, null, null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result.TotalCredit.Should().Be(50000m);
        result.TotalDebit.Should().Be(35000m);
        result.NetCashflow.Should().Be(15000m);
        result.IsCashflowPositive.Should().BeTrue();
        result.SpendingByCategory.Should().HaveCount(2);
        result.RecentTransactions.Should().HaveCount(1);
        result.PeriodStart.Should().Be(new DateOnly(2025, 1, 1));
        result.PeriodEnd.Should().Be(new DateOnly(2025, 12, 31));
    }

    /// <summary>
    /// Verifies that a user with no transactions returns zero totals.
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_WithNoTransactions_ReturnsZeroTotals()
    {
        var userId = Guid.NewGuid();

        var totals = new AnalysisService.CashflowTotals
        {
            PeriodStart = null,
            PeriodEnd = null,
            TotalCredit = 0m,
            TotalDebit = 0m
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(totals);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<CategorySpendingResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<CategorySpendingResponse>());

        _dbExecutorMock
            .Setup(x => x.QueryAsync<RecentTransactionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<RecentTransactionResponse>());

        var result = await _sut.GetSummaryAsync(userId, null, null, null, CancellationToken.None);

        result.TotalCredit.Should().Be(0);
        result.TotalDebit.Should().Be(0);
        result.NetCashflow.Should().Be(0);
        result.IsCashflowPositive.Should().BeTrue();
        result.SpendingByCategory.Should().BeEmpty();
        result.RecentTransactions.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that negative cash flow returns IsCashflowPositive as false.
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_WithNegativeCashflow_ReturnsIsCashflowPositiveFalse()
    {
        var userId = Guid.NewGuid();

        var totals = new AnalysisService.CashflowTotals
        {
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 1, 31),
            TotalCredit = 1000m,
            TotalDebit = 2000m
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(totals);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<CategorySpendingResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<CategorySpendingResponse>());

        _dbExecutorMock
            .Setup(x => x.QueryAsync<RecentTransactionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<RecentTransactionResponse>());

        var result = await _sut.GetSummaryAsync(userId, null, null, null, CancellationToken.None);

        result.NetCashflow.Should().Be(-1000m);
        result.IsCashflowPositive.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the bank account filter is included in the query when specified.
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_WithBankAccountFilter_IncludesFilterInQuery()
    {
        var userId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();

        var totals = new AnalysisService.CashflowTotals
        {
            PeriodStart = null, PeriodEnd = null, TotalCredit = 0, TotalDebit = 0
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(totals);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<CategorySpendingResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<CategorySpendingResponse>());

        _dbExecutorMock
            .Setup(x => x.QueryAsync<RecentTransactionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<RecentTransactionResponse>());

        var result = await _sut.GetSummaryAsync(userId, bankAccountId, null, null, CancellationToken.None);

        result.Should().NotBeNull();
        _dbExecutorMock.Verify(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the date range filter is included in the query when specified.
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_WithDateRange_IncludesDateFilter()
    {
        var userId = Guid.NewGuid();
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 3, 31);

        var totals = new AnalysisService.CashflowTotals
        {
            PeriodStart = null, PeriodEnd = null, TotalCredit = 0, TotalDebit = 0
        };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(totals);

        _dbExecutorMock
            .Setup(x => x.QueryAsync<CategorySpendingResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<CategorySpendingResponse>());

        _dbExecutorMock
            .Setup(x => x.QueryAsync<RecentTransactionResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<RecentTransactionResponse>());

        var result = await _sut.GetSummaryAsync(userId, null, from, to, CancellationToken.None);

        result.Should().NotBeNull();
        _dbExecutorMock.Verify(x => x.QuerySingleAsync<AnalysisService.CashflowTotals>(It.IsAny<CommandDefinition>()), Times.Once);
    }
}
