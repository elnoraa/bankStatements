using Dapper;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Data;

namespace Statements.WebAPI.Services.Statements;

public sealed class StatementService : IStatementService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IStatementParser _statementParser;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public StatementService(
        IDbConnectionFactory connectionFactory,
        IStatementParser statementParser,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _connectionFactory = connectionFactory;
        _statementParser = statementParser;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<StatementUploadResponse> UploadAsync(
        Guid userId,
        Guid? bankAccountId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only PDF bank statements are supported.");
        }

        using var connection = _connectionFactory.CreateConnection();

        if (bankAccountId is not null)
        {
            var accountBelongsToUser = await connection.QuerySingleAsync<bool>(
                new CommandDefinition(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM bank_accounts
                        WHERE id = @BankAccountId
                        AND user_id = @UserId
                    )
                    """,
                    new { BankAccountId = bankAccountId, UserId = userId },
                    cancellationToken: cancellationToken));

            if (!accountBelongsToUser)
            {
                throw new InvalidOperationException("The selected bank account does not exist for this user.");
            }
        }

        var uploadsDirectory = GetUploadsDirectory();
        Directory.CreateDirectory(uploadsDirectory);

        var originalFileName = Path.GetFileName(file.FileName);
        var storedFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}-{Guid.NewGuid():N}.pdf";
        var savedPath = Path.Combine(uploadsDirectory, storedFileName);

        await using (var stream = File.Create(savedPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        try
        {
            var statement = await connection.QuerySingleAsync<StatementUploadResponse>(
                new CommandDefinition(
                    """
                    INSERT INTO bank_statements (
                        user_id,
                        bank_account_id,
                        original_file_name,
                        stored_file_name,
                        content_type,
                        size_in_bytes,
                        status
                    )
                    VALUES (
                        @UserId,
                        @BankAccountId,
                        @OriginalFileName,
                        @StoredFileName,
                        @ContentType,
                        @SizeInBytes,
                        'uploaded'
                    )
                    RETURNING
                        id AS Id,
                        user_id AS UserId,
                        bank_account_id AS BankAccountId,
                        original_file_name AS OriginalFileName,
                        stored_file_name AS StoredFileName,
                        size_in_bytes AS SizeInBytes,
                        content_type AS ContentType,
                        status AS Status,
                        uploaded_at AS UploadedAt,
                        0 AS ParsedTransactionCount
                    """,
                    new
                    {
                        UserId = userId,
                        BankAccountId = bankAccountId,
                        OriginalFileName = originalFileName,
                        StoredFileName = storedFileName,
                        ContentType = file.ContentType,
                        SizeInBytes = file.Length
                    },
                    cancellationToken: cancellationToken));

            var transactions = _statementParser.Parse(savedPath);
            await InsertTransactionsAsync(connection, statement.Id, bankAccountId, transactions, cancellationToken);

            var processedStatement = await connection.QuerySingleAsync<StatementUploadResponse>(
                new CommandDefinition(
                    """
                    UPDATE bank_statements
                    SET
                        status = 'processed',
                        processed_at = NOW(),
                        statement_start_date = @StatementStartDate,
                        statement_end_date = @StatementEndDate
                    WHERE id = @StatementId
                    RETURNING
                        id AS Id,
                        user_id AS UserId,
                        bank_account_id AS BankAccountId,
                        original_file_name AS OriginalFileName,
                        stored_file_name AS StoredFileName,
                        size_in_bytes AS SizeInBytes,
                        content_type AS ContentType,
                        status AS Status,
                        uploaded_at AS UploadedAt,
                        @ParsedTransactionCount AS ParsedTransactionCount
                    """,
                    new
                    {
                        StatementId = statement.Id,
                        StatementStartDate = transactions.Count == 0 ? (DateOnly?)null : transactions.Min(t => t.TransactionDate),
                        StatementEndDate = transactions.Count == 0 ? (DateOnly?)null : transactions.Max(t => t.TransactionDate),
                        ParsedTransactionCount = transactions.Count
                    },
                    cancellationToken: cancellationToken));

            return processedStatement;
        }
        catch
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE bank_statements
                    SET status = 'failed'
                    WHERE stored_file_name = @StoredFileName
                    """,
                    new { StoredFileName = storedFileName },
                    cancellationToken: CancellationToken.None));

            File.Delete(savedPath);
            throw;
        }
    }

    private static async Task InsertTransactionsAsync(
        System.Data.IDbConnection connection,
        Guid statementId,
        Guid? bankAccountId,
        IReadOnlyList<ParsedStatementTransaction> transactions,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < transactions.Count; index++)
        {
            var transaction = transactions[index];

            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO statement_transactions (
                        bank_statement_id,
                        bank_account_id,
                        category_id,
                        transaction_date,
                        description,
                        merchant_name,
                        amount,
                        transaction_type,
                        balance_after,
                        external_reference
                    )
                    SELECT
                        @StatementId,
                        @BankAccountId,
                        c.id,
                        @TransactionDate,
                        @Description,
                        @MerchantName,
                        @Amount,
                        @TransactionType,
                        @BalanceAfter,
                        @ExternalReference
                    FROM transaction_categories c
                    WHERE c.name = @CategoryName
                    """,
                    new
                    {
                        StatementId = statementId,
                        BankAccountId = bankAccountId,
                        TransactionDate = transaction.TransactionDate,
                        transaction.Description,
                        MerchantName = transaction.Description,
                        transaction.Amount,
                        TransactionType = transaction.TransactionType,
                        transaction.BalanceAfter,
                        ExternalReference = $"{statementId:N}-{index + 1}",
                        transaction.CategoryName
                    },
                    cancellationToken: cancellationToken));
        }
    }

    private string GetUploadsDirectory()
    {
        var configuredDirectory = _configuration["FileStorage:UploadsDirectory"];

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        var backendDirectory = Directory.GetParent(_environment.ContentRootPath)?.FullName
            ?? _environment.ContentRootPath;

        return Path.Combine(backendDirectory, "Uploads");
    }
}
