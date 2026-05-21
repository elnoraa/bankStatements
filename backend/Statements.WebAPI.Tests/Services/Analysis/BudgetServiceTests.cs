using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Tests.Services.Analysis;

/// <summary>
/// Unit tests for <see cref="BudgetService"/>.
/// </summary>
public sealed class BudgetServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly BudgetService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateOnly _monthYear = new(2026, 5, 1);

    public BudgetServiceTests()
    {
        _sut = new BudgetService(_dbExecutorMock.Object, Mock.Of<ILogger<BudgetService>>());
    }

    [Fact]
    public async Task ListAsync_ReturnsBudgets()
    {
        var budgets = new List<BudgetResponse>
        {
            new() { Id = Guid.NewGuid(), CategoryId = Guid.NewGuid(), CategoryName = "Groceries", MonthYear = _monthYear, Amount = 500 }
        };

        _dbExecutorMock
            .Setup(x => x.QueryAsync<BudgetResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(budgets);

        var result = await _sut.ListAsync(_userId, _monthYear, CancellationToken.None);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_InsertsOrUpserts()
    {
        var response = new BudgetResponse { Id = Guid.NewGuid(), CategoryId = Guid.NewGuid(), CategoryName = "Groceries", MonthYear = _monthYear, Amount = 500 };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<BudgetResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(response);

        var request = new CreateBudgetRequest { CategoryId = Guid.NewGuid(), MonthYear = _monthYear, Amount = 500 };

        var result = await _sut.CreateOrUpdateAsync(_userId, request, CancellationToken.None);

        result.Amount.Should().Be(500);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingBudget_Deletes()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.DeleteAsync(_userId, Guid.NewGuid(), CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentBudget_Throws()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(0);

        var act = () => _sut.DeleteAsync(_userId, Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Budget not found.");
    }

    [Fact]
    public async Task GetProgressAsync_ReturnsProgress()
    {
        _dbExecutorMock
            .Setup(x => x.QueryAsync<BudgetProgressResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<BudgetProgressResponse>
            {
                new() { CategoryId = Guid.NewGuid(), CategoryName = "Groceries", BudgetAmount = 500, ActualSpending = 300 }
            });

        var result = await _sut.GetProgressAsync(_userId, _monthYear, null, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].PercentageUsed.Should().Be(60.0m);
        result[0].Remaining.Should().Be(200);
        result[0].IsOverBudget.Should().BeFalse();
    }
}
