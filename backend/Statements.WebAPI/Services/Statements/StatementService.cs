using System.Security.Cryptography;
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
    private readonly ILogger<StatementService> _logger;

    public StatementService(
        IDbConnectionFactory connectionFactory,
        IStatementParser statementParser,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<StatementService> logger)
    {
        _connectionFactory = connectionFactory;
        _statementParser = statementParser;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<StatementUploadResponse> UploadAsync(
        Guid userId,
        Guid? bankAccountId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Statement upload requested: UserId={UserId}, FileName={FileName}, Size={Size}",
            userId, file.FileName, file.Length);

        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Upload rejected - not a PDF file: {FileName}", file.FileName);
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
                _logger.LogWarning("Upload rejected - bank account {BankAccountId} does not belong to user {UserId}", bankAccountId, userId);
                throw new InvalidOperationException("The selected bank account does not exist for this user.");
            }

            _logger.LogDebug("Bank account {BankAccountId} verified for user {UserId}", bankAccountId, userId);
        }

        var uploadsDirectory = GetUploadsDirectory();
        Directory.CreateDirectory(uploadsDirectory);

        var originalFileName = Path.GetFileName(file.FileName);
        var storedFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}-{Guid.NewGuid():N}.pdf";
        var savedPath = Path.Combine(uploadsDirectory, storedFileName);

        _logger.LogDebug("Saving uploaded file to: {SavedPath}", savedPath);

        string fileHash;
        await using (var fileStream = File.Create(savedPath))
        {
            using var sha256 = SHA256.Create();
            await using var cryptoStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write);
            await file.CopyToAsync(cryptoStream, cancellationToken);
            await cryptoStream.FlushFinalBlockAsync(cancellationToken);
            fileHash = Convert.ToHexString(sha256.Hash!);
        }

        _logger.LogDebug("File saved successfully: {SavedPath} ({Size} bytes, Hash={FileHash})", savedPath, file.Length, fileHash);

        var existingStatementId = await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                """
                SELECT id
                FROM bank_statements
                WHERE user_id = @UserId AND file_hash = @FileHash
                LIMIT 1
                """,
                new { UserId = userId, FileHash = fileHash },
                cancellationToken: cancellationToken));

        if (existingStatementId is not null)
        {
            _logger.LogWarning("Duplicate upload rejected - hash {FileHash} already exists for user {UserId}", fileHash, userId);
            File.Delete(savedPath);
            throw new InvalidOperationException("This statement has already been uploaded.");
        }

        try
        {
            _logger.LogInformation("Inserting bank statement record for file: {OriginalFileName}", originalFileName);

            var statement = await connection.QuerySingleAsync<StatementUploadResponse>(
                new CommandDefinition(
                    """
                    INSERT INTO bank_statements (
                        user_id,
                        bank_account_id,
                        original_file_name,
                        stored_file_name,
                        file_hash,
                        content_type,
                        size_in_bytes,
                        status
                    )
                    VALUES (
                        @UserId,
                        @BankAccountId,
                        @OriginalFileName,
                        @StoredFileName,
                        @FileHash,
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
                        file_hash AS FileHash,
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
                        FileHash = fileHash,
                        ContentType = file.ContentType,
                        SizeInBytes = file.Length
                    },
                    cancellationToken: cancellationToken));

            _logger.LogInformation("Bank statement record created: StatementId={StatementId}, Status={Status}", statement.Id, statement.Status);

            var transactions = _statementParser.Parse(savedPath);
            _logger.LogInformation("Parsed {TransactionCount} transactions from statement {StatementId}", transactions.Count, statement.Id);

            await InsertTransactionsAsync(connection, statement.Id, bankAccountId, transactions, cancellationToken);

            _logger.LogInformation("Marking statement {StatementId} as processed with {TransactionCount} transactions",
                statement.Id, transactions.Count);

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
                        file_hash AS FileHash,
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

            _logger.LogInformation("Statement {StatementId} processed successfully", processedStatement.Id);
            return processedStatement;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process statement upload: {FileName}", originalFileName);

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

    private async Task InsertTransactionsAsync(
        System.Data.IDbConnection connection,
        Guid statementId,
        Guid? bankAccountId,
        IReadOnlyList<ParsedStatementTransaction> transactions,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Inserting {Count} transactions for statement {StatementId}", transactions.Count, statementId);

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
            _logger.LogDebug("Using configured uploads directory: {Directory}", configuredDirectory);
            return configuredDirectory;
        }

        var backendDirectory = Directory.GetParent(_environment.ContentRootPath)?.FullName
            ?? _environment.ContentRootPath;

        var uploadsPath = Path.Combine(backendDirectory, "Uploads");
        _logger.LogDebug("Using default uploads directory: {Directory}", uploadsPath);
        return uploadsPath;
    }
}
