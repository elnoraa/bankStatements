using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Analysis;

namespace Statements.WebAPI.Tests.Services.Analysis;

/// <summary>
/// Unit tests for <see cref="TransactionService"/>.
/// </summary>
public sealed class TransactionServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly TransactionService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _transactionId = Guid.NewGuid();

    public TransactionServiceTests()
    {
        _sut = new TransactionService(_dbExecutorMock.Object, Mock.Of<ILogger<TransactionService>>());
    }

    [Fact]
    public async Task UpdateAsync_WithValidRequest_UpdatesTransaction()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var request = new UpdateTransactionRequest
        {
            Description = "Updated description",
            CategoryId = Guid.NewGuid()
        };

        await _sut.UpdateAsync(_userId, _transactionId, request, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentTransaction_ThrowsInvalidOperationException()
    {
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(0);

        var act = () => _sut.UpdateAsync(_userId, _transactionId, new UpdateTransactionRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Transaction not found.");
    }

    [Fact]
    public async Task GetCategoriesAsync_ReturnsList()
    {
        var categories = new List<CategoryResponse>
        {
            new() { Id = Guid.NewGuid(), Name = "Groceries", TransactionType = "debit" },
            new() { Id = Guid.NewGuid(), Name = "Salary", TransactionType = "credit" }
        };

        _dbExecutorMock
            .Setup(x => x.QueryAsync<CategoryResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(categories);

        var result = await _sut.GetCategoriesAsync(CancellationToken.None);

        result.Should().HaveCount(2);
    }
}
