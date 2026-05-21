using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.BankAccounts;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.BankAccounts;

namespace Statements.WebAPI.Tests.Services.BankAccounts;

/// <summary>
/// Unit tests for <see cref="BankAccountService"/>.
/// </summary>
public sealed class BankAccountServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly BankAccountService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public BankAccountServiceTests()
    {
        _sut = new BankAccountService(
            _dbExecutorMock.Object,
            Mock.Of<ILogger<BankAccountService>>());
    }

    [Fact]
    public async Task ListAsync_WithValidUser_ReturnsAccounts()
    {
        var accounts = new List<BankAccountResponse>
        {
            new() { Id = Guid.NewGuid(), UserId = _userId, AccountName = "Account 1", BankName = "Bank", Currency = "AUD", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow },
            new() { Id = Guid.NewGuid(), UserId = _userId, AccountName = "Account 2", BankName = "Bank", Currency = "AUD", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }
        };

        _dbExecutorMock
            .Setup(x => x.QueryAsync<BankAccountResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(accounts);

        var result = await _sut.ListAsync(_userId, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].AccountName.Should().Be("Account 1");
        result[1].AccountName.Should().Be("Account 2");
    }

    [Fact]
    public async Task ListAsync_WithNoAccounts_ReturnsEmptyList()
    {
        _dbExecutorMock
            .Setup(x => x.QueryAsync<BankAccountResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new List<BankAccountResponse>());

        var result = await _sut.ListAsync(_userId, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithNullRequest_CreatesUntitledAccount()
    {
        var accountId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<BankAccountResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new BankAccountResponse
            {
                Id = accountId,
                UserId = _userId,
                AccountName = "Untitled",
                BankName = string.Empty,
                Currency = "AUD",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var result = await _sut.CreateAsync(_userId, null, CancellationToken.None);

        result.Id.Should().Be(accountId);
        result.AccountName.Should().Be("Untitled");
        result.BankName.Should().BeEmpty();
        result.Currency.Should().Be("AUD");
    }

    [Fact]
    public async Task CreateAsync_WithCustomName_CreatesNamedAccount()
    {
        var accountId = Guid.NewGuid();
        var request = new CreateBankAccountRequest { AccountName = "My Account", BankName = "Test Bank" };

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<BankAccountResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new BankAccountResponse
            {
                Id = accountId,
                UserId = _userId,
                AccountName = "My Account",
                BankName = "Test Bank",
                Currency = "AUD",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var result = await _sut.CreateAsync(_userId, request, CancellationToken.None);

        result.Id.Should().Be(accountId);
        result.AccountName.Should().Be("My Account");
        result.BankName.Should().Be("Test Bank");
    }

    [Fact]
    public async Task UpdateAsync_WithValidAccount_UpdatesName()
    {
        var accountId = Guid.NewGuid();
        var request = new UpdateBankAccountRequest { AccountName = "Renamed Account", BankName = null };

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<BankAccountResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new BankAccountResponse
            {
                Id = accountId,
                UserId = _userId,
                AccountName = "Renamed Account",
                BankName = "Existing Bank",
                Currency = "AUD",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var result = await _sut.UpdateAsync(_userId, accountId, request, CancellationToken.None);

        result.AccountName.Should().Be("Renamed Account");
    }

    [Fact]
    public async Task UpdateAsync_WithNonexistentAccount_ThrowsInvalidOperationException()
    {
        var accountId = Guid.NewGuid();
        var request = new UpdateBankAccountRequest { AccountName = "New Name", BankName = null };

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<BankAccountResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((BankAccountResponse?)null);

        var act = () => _sut.UpdateAsync(_userId, accountId, request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Bank account not found.");
    }

    [Fact]
    public async Task DeleteAsync_WithValidAccount_DeletesAndReturns()
    {
        var accountId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.DeleteAsync(_userId, accountId, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonexistentAccount_ThrowsInvalidOperationException()
    {
        var accountId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(0);

        var act = () => _sut.DeleteAsync(_userId, accountId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Bank account not found.");
    }
}
