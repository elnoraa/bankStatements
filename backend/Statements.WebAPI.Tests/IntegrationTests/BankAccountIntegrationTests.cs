using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.BankAccounts;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.BankAccounts;

namespace Statements.WebAPI.Tests.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="BankAccountService"/> using a real PostgreSQL database via Testcontainers.
/// Tests CRUD operations and CASCADE delete behavior.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BankAccountIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly IDbExecutor _dbExecutor;
    private readonly BankAccountService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public BankAccountIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString
            })
            .Build();

        var connectionFactory = new NpgsqlConnectionFactory(
            config,
            Mock.Of<ILogger<NpgsqlConnectionFactory>>());

        _dbExecutor = new DapperDbExecutor(connectionFactory);
        _sut = new BankAccountService(_dbExecutor, Mock.Of<ILogger<BankAccountService>>());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.ClearDataAsync();
    }

    [Fact]
    public async Task CreateAsync_WithNoName_CreatesUntitledAccount()
    {
        // Arrange: create a user first
        await SeedUserAsync(_userId);

        // Act
        var result = await _sut.CreateAsync(_userId, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.AccountName.Should().Be("Untitled");
        result.BankName.Should().BeEmpty();
        result.Currency.Should().Be("AUD");
        result.UserId.Should().Be(_userId);

        // Verify persisted
        var dbAccount = await _dbExecutor.QuerySingleAsync<BankAccountResponse>(
            new CommandDefinition(
                "SELECT id AS Id, user_id AS UserId, bank_name AS BankName, account_name AS AccountName, currency AS Currency, created_at AS CreatedAt, updated_at AS UpdatedAt FROM bank_accounts WHERE id = @Id",
                new { Id = result.Id }));
        dbAccount.AccountName.Should().Be("Untitled");
    }

    [Fact]
    public async Task CreateAsync_WithCustomName_CreatesNamedAccount()
    {
        // Arrange
        await SeedUserAsync(_userId);
        var request = new CreateBankAccountRequest("My Account", "Test Bank");

        // Act
        var result = await _sut.CreateAsync(_userId, request, CancellationToken.None);

        // Assert
        result.AccountName.Should().Be("My Account");
        result.BankName.Should().Be("Test Bank");
        result.Currency.Should().Be("AUD");

        // Verify persisted
        var dbAccount = await _dbExecutor.QuerySingleAsync<BankAccountResponse>(
            new CommandDefinition(
                "SELECT id AS Id, user_id AS UserId, bank_name AS BankName, account_name AS AccountName, currency AS Currency FROM bank_accounts WHERE id = @Id",
                new { Id = result.Id }));
        dbAccount.AccountName.Should().Be("My Account");
        dbAccount.BankName.Should().Be("Test Bank");
    }

    [Fact]
    public async Task ListAsync_WithMultipleAccounts_ReturnsAllOrderedByCreation()
    {
        // Arrange
        await SeedUserAsync(_userId);
        var account1Id = Guid.NewGuid();
        var account2Id = Guid.NewGuid();

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bank_accounts (id, user_id, bank_name, account_name, currency, created_at) VALUES (@Id, @UserId, '', 'Account B', 'AUD', @CreatedAt)",
                new { Id = account1Id, UserId = _userId, CreatedAt = DateTime.UtcNow.AddMinutes(-10) }));
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bank_accounts (id, user_id, bank_name, account_name, currency, created_at) VALUES (@Id, @UserId, '', 'Account A', 'AUD', @CreatedAt)",
                new { Id = account2Id, UserId = _userId, CreatedAt = DateTime.UtcNow }));

        // Act
        var accounts = await _sut.ListAsync(_userId, CancellationToken.None);

        // Assert — ordered by created_at ASC, so Account B (older) first, then Account A
        accounts.Should().HaveCount(2);
        accounts[0].AccountName.Should().Be("Account B");
        accounts[1].AccountName.Should().Be("Account A");
    }

    [Fact]
    public async Task UpdateAsync_WithValidId_UpdatesName()
    {
        // Arrange
        await SeedUserAsync(_userId);
        var accountId = await SeedAccountAsync(_userId, "Original Name");
        var request = new UpdateBankAccountRequest("Renamed", "New Bank");

        // Act
        var result = await _sut.UpdateAsync(_userId, accountId, request, CancellationToken.None);

        // Assert
        result.AccountName.Should().Be("Renamed");

        // Verify persisted
        var dbAccount = await _dbExecutor.QuerySingleAsync<BankAccountResponse>(
            new CommandDefinition(
                "SELECT id AS Id, user_id AS UserId, bank_name AS BankName, account_name AS AccountName FROM bank_accounts WHERE id = @Id",
                new { Id = accountId }));
        dbAccount.AccountName.Should().Be("Renamed");
        dbAccount.BankName.Should().Be("New Bank");
    }

    [Fact]
    public async Task DeleteAsync_WithStatements_CascadesDelete()
    {
        // Arrange
        await SeedUserAsync(_userId);
        var accountId = await SeedAccountAsync(_userId, "Test Account");

        // Insert a statement + transaction linked to the account
        var statementId = Guid.NewGuid();
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO bank_statements (id, user_id, bank_account_id, original_file_name, stored_file_name, file_hash, content_type, size_in_bytes, status)
                VALUES (@Id, @UserId, @BankAccountId, @FileName, @StoredName, @FileHash, 'application/pdf', 1024, 'processed')
                """,
                new
                {
                    Id = statementId,
                    UserId = _userId,
                    BankAccountId = accountId,
                    FileName = "test.pdf",
                    StoredName = $"test-{statementId:N}.pdf",
                    FileHash = $"HASH{statementId:N}"
                }));

        var transactionId = Guid.NewGuid();
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO statement_transactions (id, bank_statement_id, bank_account_id, transaction_date, description, amount, transaction_type)
                VALUES (@Id, @StatementId, @BankAccountId, @Date, @Desc, @Amount, @Type)
                """,
                new
                {
                    Id = transactionId,
                    StatementId = statementId,
                    BankAccountId = accountId,
                    Date = new DateOnly(2026, 1, 15),
                    Desc = "Test transaction",
                    Amount = 100.00m,
                    Type = "debit"
                }));

        // Act
        await _sut.DeleteAsync(_userId, accountId, CancellationToken.None);

        // Assert — CASCADE should remove everything
        var accountCount = await _dbExecutor.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM bank_accounts WHERE id = @Id",
                new { Id = accountId }));
        accountCount.Should().Be(0);

        var statementCount = await _dbExecutor.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM bank_statements WHERE id = @Id",
                new { Id = statementId }));
        statementCount.Should().Be(0);

        var transactionCount = await _dbExecutor.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM statement_transactions WHERE id = @Id",
                new { Id = transactionId }));
        transactionCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_WithNonexistentAccount_ThrowsInvalidOperationException()
    {
        // Arrange
        await SeedUserAsync(_userId);
        var nonExistentId = Guid.NewGuid();

        // Act
        var act = () => _sut.DeleteAsync(_userId, nonExistentId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Bank account not found.");
    }

    // ── Helpers ─────────────────────────────────────────

    /// <summary>
    /// Seeds a minimal user row for test isolation.
    /// </summary>
    private async Task SeedUserAsync(Guid userId)
    {
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO app_users (id, email, display_name, password_hash) VALUES (@Id, @Email, @DisplayName, @PasswordHash)",
                new
                {
                    Id = userId,
                    Email = $"{userId:N}@test.com",
                    DisplayName = "Test User",
                    PasswordHash = "not-a-real-hash"
                }));
    }

    /// <summary>
    /// Seeds a bank account belonging to the specified user.
    /// Returns the account ID.
    /// </summary>
    private async Task<Guid> SeedAccountAsync(Guid userId, string accountName)
    {
        var accountId = Guid.NewGuid();
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bank_accounts (id, user_id, bank_name, account_name, currency) VALUES (@Id, @UserId, '', @AccountName, 'AUD')",
                new { Id = accountId, UserId = userId, AccountName = accountName }));
        return accountId;
    }
}
