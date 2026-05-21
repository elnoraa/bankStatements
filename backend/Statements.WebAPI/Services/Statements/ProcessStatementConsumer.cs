using Dapper;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Processes a <see cref="ProcessStatementMessage"/> by parsing the PDF file
/// and inserting the extracted transactions into the database.
/// </summary>
public sealed class ProcessStatementConsumer
{
    private readonly IDbExecutor _dbExecutor;
    private readonly IStatementParser _statementParser;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessStatementConsumer> _logger;

    public ProcessStatementConsumer(
        IDbExecutor dbExecutor,
        IStatementParser statementParser,
        IConfiguration configuration,
        ILogger<ProcessStatementConsumer> logger)
    {
        _dbExecutor = dbExecutor;
        _statementParser = statementParser;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ConsumeAsync(ProcessStatementMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing statement: StatementId={StatementId}", message.StatementId);

        // Step 1: Idempotency check — skip if already processed or failed
        var currentStatus = await _dbExecutor.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT status FROM bank_statements WHERE id = @StatementId
                """,
                new { StatementId = message.StatementId },
                cancellationToken: cancellationToken));

        if (currentStatus is "processed" or "failed")
        {
            _logger.LogInformation(
                "Statement {StatementId} already {Status}, skipping",
                message.StatementId, currentStatus);
            return;
        }

        // Step 2: Update status to 'processing'
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE bank_statements
                SET status = 'processing'
                WHERE id = @StatementId AND status = 'uploaded'
                """,
                new { StatementId = message.StatementId },
                cancellationToken: cancellationToken));

        // Step 3: Get stored file path
        var statement = await _dbExecutor.QuerySingleAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT stored_file_name, bank_account_id
                FROM bank_statements
                WHERE id = @StatementId
                """,
                new { StatementId = message.StatementId },
                cancellationToken: cancellationToken));

        string storedFileName = statement.stored_file_name;
        Guid bankAccountId = statement.bank_account_id;

        var uploadsDir = _configuration["FileStorage:UploadsDirectory"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        var filePath = Path.Combine(uploadsDir, storedFileName);

        // Step 4: Parse PDF (CPU-bound, synchronous)
        IReadOnlyList<ParsedStatementTransaction> transactions;
        try
        {
            transactions = _statementParser.Parse(filePath);
            _logger.LogInformation(
                "Parsed {Count} transactions from statement {StatementId}",
                transactions.Count, message.StatementId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF parsing failed for statement {StatementId}", message.StatementId);
            await MarkFailedAsync(message.StatementId, $"PDF parsing failed: {ex.Message}");
            throw;
        }

        // Step 5: Insert transactions
        try
        {
            for (var index = 0; index < transactions.Count; index++)
            {
                var t = transactions[index];
                await _dbExecutor.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO statement_transactions (
                            bank_statement_id, bank_account_id, category_id,
                            transaction_date, description, merchant_name,
                            amount, transaction_type, balance_after, external_reference
                        )
                        SELECT @StatementId, @BankAccountId, c.id,
                            @TransactionDate, @Description, @MerchantName,
                            @Amount, @TransactionType, @BalanceAfter, @ExternalReference
                        FROM transaction_categories c
                        WHERE c.name = @CategoryName
                        """,
                        new
                        {
                            StatementId = message.StatementId,
                            BankAccountId = bankAccountId,
                            TransactionDate = t.TransactionDate,
                            Description = t.Description,
                            MerchantName = t.Description,
                            Amount = t.Amount,
                            TransactionType = t.TransactionType,
                            BalanceAfter = t.BalanceAfter,
                            ExternalReference = $"{message.StatementId:N}-{index + 1}",
                            CategoryName = t.CategoryName
                        },
                        cancellationToken: cancellationToken));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Transaction insertion failed for statement {StatementId}",
                message.StatementId);
            await MarkFailedAsync(message.StatementId, $"Transaction insertion failed: {ex.Message}");
            throw;
        }

        // Step 6: Mark as processed
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE bank_statements
                SET status = 'processed',
                    processed_at = NOW(),
                    statement_start_date = @StartDate,
                    statement_end_date = @EndDate
                WHERE id = @StatementId
                """,
                new
                {
                    StatementId = message.StatementId,
                    StartDate = transactions.Count == 0
                        ? (DateOnly?)null
                        : transactions.Min(t => t.TransactionDate),
                    EndDate = transactions.Count == 0
                        ? (DateOnly?)null
                        : transactions.Max(t => t.TransactionDate)
                },
                cancellationToken: cancellationToken));

        _logger.LogInformation(
            "Statement {StatementId} processed successfully with {Count} transactions",
            message.StatementId, transactions.Count);
    }

    private async Task MarkFailedAsync(Guid statementId, string errorMessage)
    {
        try
        {
            await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE bank_statements
                    SET status = 'failed',
                        failed_at = NOW(),
                        error_message = @ErrorMessage
                    WHERE id = @StatementId
                    """,
                    new { StatementId = statementId, ErrorMessage = errorMessage },
                    cancellationToken: CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to mark statement {StatementId} as failed (best-effort)",
                statementId);
        }
    }
}
