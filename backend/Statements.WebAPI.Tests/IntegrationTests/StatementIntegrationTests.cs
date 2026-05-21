using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Analysis;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Analysis;
using Statements.WebAPI.Services.Export;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.IntegrationTests;

/// <summary>
/// Integration tests for statement processing, transaction editing, CSV export, and budgets.
/// Uses Testcontainers PostgreSQL via <see cref="DatabaseFixture"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StatementIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly IDbExecutor _dbExecutor;
    private readonly TransactionService _transactionService;
    private readonly BudgetService _budgetService;
    private readonly CsvExportService _csvExportService;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _bankAccountId = Guid.NewGuid();

    public StatementIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString
            })
            .Build();

        var connectionFactory = new NpgsqlConnectionFactory(
            config, Mock.Of<ILogger<NpgsqlConnectionFactory>>());

        _dbExecutor = new DapperDbExecutor(connectionFactory);
        _transactionService = new TransactionService(_dbExecutor, Mock.Of<ILogger<TransactionService>>());
        _budgetService = new BudgetService(_dbExecutor, Mock.Of<ILogger<BudgetService>>());
        _csvExportService = new CsvExportService(_dbExecutor);
    }

    public Task InitializeAsync() => SeedDataAsync();

    public async Task DisposeAsync()
    {
        await _fixture.ClearDataAsync();
    }

    private async Task SeedDataAsync()
    {
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition("INSERT INTO app_users (id, email, display_name) VALUES (@Id, @Email, @Name)",
                new { Id = _userId, Email = "test@example.com", Name = "Test User" }));

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition("INSERT INTO bank_accounts (id, user_id, bank_name, account_name) VALUES (@Id, @UserId, @Bank, @Acct)",
                new { Id = _bankAccountId, UserId = _userId, Bank = "Test Bank", Acct = "Test Account" }));
    }

    private async Task<Guid> SeedStatementAsync(string status = "processed")
    {
        var statementId = Guid.NewGuid();
        var fileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("test"u8));
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO bank_statements (id, user_id, bank_account_id, original_file_name, stored_file_name, file_hash, size_in_bytes, status, processed_at)
                VALUES (@Id, @UserId, @BankAccountId, @FileName, @StoredName, @Hash, 100, @Status, NOW())
                """,
                new
                {
                    Id = statementId, UserId = _userId, BankAccountId = _bankAccountId,
                    FileName = "test.pdf", StoredName = $"test-{Guid.NewGuid():N}.pdf",
                    Hash = fileHash, Status = status
                }));
        return statementId;
    }

    private async Task<Guid> SeedTransactionAsync(Guid statementId, Guid? categoryId = null, decimal amount = 50.00m)
    {
        var txId = Guid.NewGuid();
        // Get a category if none specified
        if (categoryId is null)
        {
            categoryId = await _dbExecutor.QuerySingleAsync<Guid>(
                new CommandDefinition("SELECT id FROM transaction_categories WHERE name = 'Groceries' LIMIT 1"));
        }
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO statement_transactions (id, bank_statement_id, bank_account_id, category_id, transaction_date, description, amount, transaction_type)
                VALUES (@Id, @StatementId, @BankAccountId, @CategoryId, @Date, @Desc, @Amount, 'debit')
                """,
                new
                {
                    Id = txId, StatementId = statementId, BankAccountId = _bankAccountId,
                    CategoryId = categoryId, Date = new DateOnly(2026, 5, 15), Desc = "Test transaction",
                    Amount = amount
                }));
        return txId;
    }

    [Fact]
    public async Task TransactionService_Update_ModifiesTransaction()
    {
        var statementId = await SeedStatementAsync();
        var categoryId = await _dbExecutor.QuerySingleAsync<Guid>(
            new CommandDefinition("SELECT id FROM transaction_categories WHERE name = 'Dining' LIMIT 1"));
        var txId = await SeedTransactionAsync(statementId);

        var request = new UpdateTransactionRequest { Description = "Updated desc", CategoryId = categoryId };
        await _transactionService.UpdateAsync(_userId, txId, request, CancellationToken.None);

        var result = await _dbExecutor.QuerySingleAsync<(string desc, Guid? catId)>(
            new CommandDefinition("SELECT description, category_id FROM statement_transactions WHERE id = @Id", new { Id = txId }));

        result.desc.Should().Be("Updated desc");
        result.catId.Should().Be(categoryId);
    }

    [Fact]
    public async Task CsvExportService_ReturnsValidCsv()
    {
        var statementId = await SeedStatementAsync();
        await SeedTransactionAsync(statementId);

        var csvBytes = await _csvExportService.ExportTransactionsAsync(_userId, null, null, null, CancellationToken.None);
        var csv = System.Text.Encoding.UTF8.GetString(csvBytes);

        csv.Should().StartWith("Date,Description,Category,Amount,Type");
        csv.Should().Contain("Test transaction");
        csv.Should().Contain("Groceries");
    }

    [Fact]
    public async Task BudgetService_CreateAndList_Works()
    {
        var categoryId = await _dbExecutor.QuerySingleAsync<Guid>(
            new CommandDefinition("SELECT id FROM transaction_categories WHERE name = 'Groceries' LIMIT 1"));

        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            MonthYear = new DateOnly(2026, 5, 1),
            Amount = 500
        };

        var created = await _budgetService.CreateOrUpdateAsync(_userId, request, CancellationToken.None);
        created.Amount.Should().Be(500);

        var budgets = await _budgetService.ListAsync(_userId, new DateOnly(2026, 5, 1), CancellationToken.None);
        budgets.Should().HaveCount(1);
        budgets[0].CategoryName.Should().Be("Groceries");
    }

    [Fact]
    public async Task BudgetService_GetProgress_CalculatesCorrectly()
    {
        var categoryId = await _dbExecutor.QuerySingleAsync<Guid>(
            new CommandDefinition("SELECT id FROM transaction_categories WHERE name = 'Groceries' LIMIT 1"));

        // Create budget of $500
        var request = new CreateBudgetRequest
        {
            CategoryId = categoryId,
            MonthYear = new DateOnly(2026, 5, 1),
            Amount = 500
        };
        await _budgetService.CreateOrUpdateAsync(_userId, request, CancellationToken.None);

        // Add a statement with a $100 grocery transaction
        var statementId = await SeedStatementAsync();
        await SeedTransactionAsync(statementId, categoryId, 100);

        var progress = await _budgetService.GetProgressAsync(_userId, new DateOnly(2026, 5, 1), null, CancellationToken.None);

        progress.Should().HaveCount(1);
        progress[0].BudgetAmount.Should().Be(500);
        progress[0].ActualSpending.Should().Be(100);
        progress[0].PercentageUsed.Should().Be(20.0m);
        progress[0].IsOverBudget.Should().BeFalse();
    }
}
