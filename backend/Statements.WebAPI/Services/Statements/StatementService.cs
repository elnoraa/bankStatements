using System.Security.Cryptography;
using Dapper;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Messaging;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Handles bank statement file upload, virus scanning, deduplication, and
/// publishes a message for background PDF parsing and transaction extraction.
/// </summary>
public sealed class StatementService : IStatementService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly IVirusScanService _virusScanService;
    private readonly IMessagePublisher _messagePublisher;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatementService> _logger;

    public StatementService(
        IDbExecutor dbExecutor,
        IVirusScanService virusScanService,
        IMessagePublisher messagePublisher,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<StatementService> logger)
    {
        _dbExecutor = dbExecutor;
        _virusScanService = virusScanService;
        _messagePublisher = messagePublisher;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StatementUploadResponse> UploadAsync(
        Guid userId,
        Guid bankAccountId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Statement upload requested: UserId={UserId}, FileName={FileName}, Size={Size}",
            userId, file.FileName, file.Length);

        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Upload rejected - not a PDF file: {FileName}", file.FileName);
            throw new InvalidOperationException("Only PDF bank statements are supported.");
        }

        // Verify PDF magic bytes (%PDF at offset 0) to reject files that aren't actually PDFs
        using (var magicStream = file.OpenReadStream())
        {
            var header = new byte[4];
            var bytesRead = await magicStream.ReadAsync(header, 0, 4, cancellationToken);
            if (bytesRead < 4 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46)
            {
                _logger.LogWarning("Upload rejected - file signature does not match PDF: {FileName}", file.FileName);
                throw new InvalidOperationException("Only PDF bank statements are supported.");
            }
        }

        var accountBelongsToUser = await _dbExecutor.QuerySingleAsync<bool>(
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

        // Virus scan the uploaded file before accepting it
        var scanResult = await _virusScanService.ScanAsync(savedPath, cancellationToken);

        if (!scanResult.IsClean)
        {
            File.Delete(savedPath);
            var virusName = scanResult.VirusName;
            _logger.LogWarning(
                "Upload rejected — virus detected in {FileName}: {VirusName} (scan took {DurationMs}ms)",
                originalFileName, virusName ?? "unknown", scanResult.Duration.TotalMilliseconds);

            if (virusName is not null)
            {
                throw new InvalidOperationException(
                    $"Upload rejected: a virus was detected ({virusName}).");
            }

            throw new InvalidOperationException(
                "Upload rejected: the file could not be verified as safe. Please try again later.");
        }

        _logger.LogDebug(
            "Virus scan passed for {FileName} ({DurationMs}ms)",
            originalFileName, scanResult.Duration.TotalMilliseconds);

        var existingId = await _dbExecutor.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                """
                SELECT id
                FROM bank_statements
                WHERE user_id = @UserId AND file_hash = @FileHash
                LIMIT 1
                """,
                new { UserId = userId, FileHash = fileHash },
                cancellationToken: cancellationToken));

        if (existingId is not null)
        {
            var existingStatus = await _dbExecutor.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    """
                    SELECT status
                    FROM bank_statements
                    WHERE id = @Id
                    """,
                    new { Id = existingId.Value },
                    cancellationToken: cancellationToken)) ?? "uploaded";

            _logger.LogInformation("Duplicate file detected: {FileName} already uploaded as statement {StatementId} (status: {Status})",
                originalFileName, existingId, existingStatus);

            File.Delete(savedPath);

            return new StatementUploadResponse
            {
                Id = existingId.Value,
                UserId = userId,
                BankAccountId = bankAccountId,
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                FileHash = fileHash,
                SizeInBytes = file.Length,
                ContentType = file.ContentType,
                Status = existingStatus,
                UploadedAt = DateTimeOffset.UtcNow,
                ParsedTransactionCount = 0
            };
        }

        try
        {
            _logger.LogDebug("Inserting bank statement record for file: {OriginalFileName}", originalFileName);

            var statement = await _dbExecutor.QuerySingleAsync<StatementUploadResponse>(
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

            // Publish message for background processing instead of parsing synchronously
            try
            {
                await _messagePublisher.PublishAsync(new ProcessStatementMessage
                {
                    StatementId = statement.Id,
                    StoredFileName = storedFileName,
                    UserId = userId,
                    BankAccountId = bankAccountId
                }, cancellationToken);

                _logger.LogInformation(
                    "Statement {StatementId} uploaded and message published for background processing",
                    statement.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish message for statement {StatementId}. " +
                    "Statement remains in 'uploaded' status and can be retried from the UI.",
                    statement.Id);
                // Don't throw — the file was saved and the DB record created.
                // The user can retry processing from the statement management UI.
                // The statement stays as 'uploaded' (not 'failed') so the retry endpoint picks it up.
            }

            return statement;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to upload statement: {FileName}", originalFileName);

            await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE bank_statements
                    SET status = 'failed'
                    WHERE stored_file_name = @StoredFileName
                    """,
                    new { StoredFileName = storedFileName },
                    cancellationToken: CancellationToken.None));

            File.Delete(savedPath);
            throw new InvalidOperationException("Failed to upload the statement. Please try again.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<StatementUploadResponse?> GetStatementAsync(
        Guid userId,
        Guid statementId,
        CancellationToken cancellationToken)
    {
        return await _dbExecutor.QuerySingleOrDefaultAsync<StatementUploadResponse>(
            new CommandDefinition(
                """
                SELECT
                    bs.id AS Id,
                    bs.user_id AS UserId,
                    bs.bank_account_id AS BankAccountId,
                    bs.original_file_name AS OriginalFileName,
                    bs.stored_file_name AS StoredFileName,
                    bs.file_hash AS FileHash,
                    bs.content_type AS ContentType,
                    bs.size_in_bytes AS SizeInBytes,
                    bs.status AS Status,
                    bs.uploaded_at AS UploadedAt,
                    COALESCE(
                        (SELECT COUNT(*)::int FROM statement_transactions st
                         WHERE st.bank_statement_id = bs.id),
                        0
                    ) AS ParsedTransactionCount,
                    bs.processed_at AS ProcessedAt,
                    bs.error_message AS ErrorMessage
                FROM bank_statements bs
                WHERE bs.id = @StatementId AND bs.user_id = @UserId
                """,
                new { StatementId = statementId, UserId = userId },
                cancellationToken: cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementListItemResponse>> ListAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = (page - 1) * pageSize;
        return (await _dbExecutor.QueryAsync<StatementListItemResponse>(
            new CommandDefinition(
                """
                SELECT
                    bs.id AS Id,
                    bs.original_file_name AS OriginalFileName,
                    bs.status AS Status,
                    bs.uploaded_at AS UploadedAt,
                    bs.processed_at AS ProcessedAt,
                    bs.failed_at AS FailedAt,
                    COALESCE(
                        (SELECT COUNT(*)::int FROM statement_transactions st
                         WHERE st.bank_statement_id = bs.id), 0
                    ) AS ParsedTransactionCount,
                    bs.size_in_bytes AS SizeInBytes,
                    bs.error_message AS ErrorMessage,
                    bs.bank_account_id AS BankAccountId
                FROM bank_statements bs
                WHERE bs.user_id = @UserId
                ORDER BY bs.uploaded_at DESC
                LIMIT @PageSize OFFSET @Offset
                """,
                new { UserId = userId, PageSize = pageSize, Offset = offset },
                cancellationToken: cancellationToken)))
            .AsList();
    }

    /// <inheritdoc />
    public async Task RetryAsync(Guid userId, Guid statementId, CancellationToken cancellationToken)
    {
        var statement = await _dbExecutor.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT status, stored_file_name, bank_account_id
                FROM bank_statements
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = statementId, UserId = userId },
                cancellationToken: cancellationToken));

        if (statement is null)
            throw new InvalidOperationException("Statement not found.");
        if (statement.status != "failed")
            throw new InvalidOperationException("Only failed statements can be retried.");

        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE bank_statements
                SET status = 'uploaded', error_message = NULL, failed_at = NULL
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = statementId, UserId = userId },
                cancellationToken: cancellationToken));

        try
        {
            await _messagePublisher.PublishAsync(new ProcessStatementMessage
            {
                StatementId = statementId,
                StoredFileName = statement.stored_file_name,
                UserId = userId,
                BankAccountId = statement.bank_account_id
            }, cancellationToken);

            _logger.LogInformation(
                "Statement {StatementId} queued for reprocessing by user {UserId}",
                statementId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish retry message for statement {StatementId}. " +
                "Statement status remains 'uploaded'.",
                statementId);
            // Don't throw — the status was already reset to 'uploaded'.
            // The user can retry again from the UI.
        }
    }

    /// <inheritdoc />
    public async Task<BulkUploadResponse> UploadMultipleAsync(
        Guid userId,
        Guid bankAccountId,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bulk upload requested: UserId={UserId}, FileCount={Count}", userId, files.Count);

        var results = new List<SingleFileUploadResult>(files.Count);
        var successCount = 0;
        var failureCount = 0;

        foreach (var file in files)
        {
            try
            {
                var response = await UploadAsync(userId, bankAccountId, file, cancellationToken);
                results.Add(new SingleFileUploadResult
                {
                    FileName = file.FileName,
                    Success = true,
                    Response = response
                });
                successCount++;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Bulk upload file failed: {FileName} - {Message}", file.FileName, ex.Message);
                results.Add(new SingleFileUploadResult
                {
                    FileName = file.FileName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
                failureCount++;
            }
        }

        _logger.LogInformation("Bulk upload completed: Success={Success}, Failed={Failed}", successCount, failureCount);
        return new BulkUploadResponse
        {
            Results = results.AsReadOnly(),
            SuccessCount = successCount,
            FailureCount = failureCount
        };
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, Guid statementId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting statement {StatementId} for user {UserId}", statementId, userId);

        var statement = await _dbExecutor.QuerySingleOrDefaultAsync<StatementUploadResponse>(
            new CommandDefinition(
                """
                SELECT id AS Id, stored_file_name AS StoredFileName
                FROM bank_statements
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = statementId, UserId = userId },
                cancellationToken: cancellationToken));

        if (statement is null)
        {
            _logger.LogWarning("Delete failed - statement {StatementId} not found for user {UserId}", statementId, userId);
            throw new InvalidOperationException("Statement not found.");
        }

        // Delete transactions cascade through FK, so just delete the statement record
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM bank_statements WHERE id = @Id AND user_id = @UserId",
                new { Id = statementId, UserId = userId },
                cancellationToken: cancellationToken));

        // Clean up the stored file
        var uploadsDir = GetUploadsDirectory();
        var filePath = Path.Combine(uploadsDir, statement.StoredFileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Deleted stored file: {FilePath}", filePath);
        }

        _logger.LogInformation("Statement {StatementId} deleted successfully", statementId);
    }

    /// <inheritdoc />
    public async Task<(string FilePath, string OriginalFileName)> DownloadOriginalAsync(
        Guid userId, Guid statementId, CancellationToken cancellationToken)
    {
        var statement = await _dbExecutor.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT stored_file_name, original_file_name
                FROM bank_statements
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = statementId, UserId = userId },
                cancellationToken: cancellationToken));

        if (statement is null)
        {
            throw new InvalidOperationException("Statement not found.");
        }

        var uploadsDir = GetUploadsDirectory();
        var filePath = Path.Combine(uploadsDir, (string)statement.stored_file_name);

        if (!File.Exists(filePath))
        {
            _logger.LogError("Original file missing for statement {StatementId}: {FilePath}", statementId, filePath);
            throw new InvalidOperationException("Original file is no longer available.");
        }

        return (filePath, (string)statement.original_file_name);
    }

    /// <inheritdoc />
    public async Task ArchiveAsync(Guid userId, Guid statementId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Archiving statement {StatementId} for user {UserId}", statementId, userId);

        var rows = await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE bank_statements
                SET status = 'archived', archived_at = NOW()
                WHERE id = @Id AND user_id = @UserId AND status != 'archived'
                """,
                new { Id = statementId, UserId = userId },
                cancellationToken: cancellationToken));

        if (rows == 0)
        {
            _logger.LogWarning("Archive failed - statement {StatementId} not found or already archived for user {UserId}", statementId, userId);
            throw new InvalidOperationException("Statement not found or already archived.");
        }

        _logger.LogInformation("Statement {StatementId} archived successfully", statementId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StatementListItemResponse>> ListArchivedAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var offset = (page - 1) * pageSize;
        return (await _dbExecutor.QueryAsync<StatementListItemResponse>(
            new CommandDefinition(
                """
                SELECT
                    bs.id AS Id,
                    bs.original_file_name AS OriginalFileName,
                    bs.status AS Status,
                    bs.uploaded_at AS UploadedAt,
                    bs.processed_at AS ProcessedAt,
                    bs.failed_at AS FailedAt,
                    COALESCE(
                        (SELECT COUNT(*)::int FROM statement_transactions st
                         WHERE st.bank_statement_id = bs.id), 0
                    ) AS ParsedTransactionCount,
                    bs.size_in_bytes AS SizeInBytes,
                    bs.error_message AS ErrorMessage,
                    bs.bank_account_id AS BankAccountId
                FROM bank_statements bs
                WHERE bs.user_id = @UserId AND bs.status = 'archived'
                ORDER BY bs.uploaded_at DESC
                LIMIT @PageSize OFFSET @Offset
                """,
                new { UserId = userId, PageSize = pageSize, Offset = offset },
                cancellationToken: cancellationToken)))
            .AsList();
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
