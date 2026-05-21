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
        _logger.LogInformation("Statement upload requested: UserId={UserId}, FileName={FileName}, Size={Size}",
            userId, file.FileName, file.Length);

        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Upload rejected - not a PDF file: {FileName}", file.FileName);
            throw new InvalidOperationException("Only PDF bank statements are supported.");
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

        _logger.LogInformation(
            "Virus scan passed for {FileName} ({DurationMs}ms)",
            originalFileName, scanResult.Duration.TotalMilliseconds);

        var existingStatementId = await _dbExecutor.QuerySingleOrDefaultAsync<Guid?>(
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
            _logger.LogInformation("Duplicate file detected: {FileName} already uploaded as statement {StatementId}",
                originalFileName, existingStatementId);

            File.Delete(savedPath);

            return new StatementUploadResponse
            {
                Id = existingStatementId.Value,
                UserId = userId,
                BankAccountId = bankAccountId,
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                FileHash = fileHash,
                SizeInBytes = file.Length,
                ContentType = file.ContentType,
                Status = "uploaded",
                UploadedAt = DateTimeOffset.UtcNow,
                ParsedTransactionCount = 0
            };
        }

        try
        {
            _logger.LogInformation("Inserting bank statement record for file: {OriginalFileName}", originalFileName);

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
