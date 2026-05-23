using System.Globalization;
using System.Net.Mail;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Statements.WebAPI.Contracts.Basiq;
using Statements.WebAPI.Data;
using Statements.WebAPI.Hubs;

namespace Statements.WebAPI.Services.Basiq;

public sealed class BasiqService : IBasiqService
{
    private readonly IDbExecutor _dbExecutor;
    private readonly IBasiqApiClient _apiClient;
    private readonly IHubContext<StatementProcessingHub> _hubContext;
    private readonly BasiqOptions _options;
    private readonly ILogger<BasiqService> _logger;

    public BasiqService(
        IDbExecutor dbExecutor,
        IBasiqApiClient apiClient,
        IHubContext<StatementProcessingHub> hubContext,
        IOptions<BasiqOptions> options,
        ILogger<BasiqService> logger)
    {
        _dbExecutor = dbExecutor;
        _apiClient = apiClient;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    // ─── Initiate Connection ───────────────────────────────────────────────

    public async Task<InitiateConnectionResponse> InitiateConnectionAsync(
        Guid userId, InitiateConnectionRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Initiating Basiq connection for user {UserId}, institution: {Institution}",
            userId, request.InstitutionName);

        // Get user's email for Basiq user creation
        var email = await _dbExecutor.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT email FROM app_users WHERE id = @UserId",
                new { UserId = userId },
                cancellationToken: ct));

        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("User not found.");

        // Basiq requires a valid RFC-compliant email. If the user's stored
        // email is invalid (e.g. external auth fallback like
        // "google-12345@noemail.local"), generate a synthetic one.
        if (!MailAddress.TryCreate(email, out _))
        {
            email = $"user-{userId:N}@bankstatements.app";
            _logger.LogWarning(
                "User {UserId} has an invalid email format; using synthetic email {Email} for Basiq.",
                userId, email);
        }

        // Get or create Basiq user for this app user
        var basiqUserId = await GetOrCreateBasiqUserIdAsync(userId, email, ct);

        // Generate client token for consent UI
        var clientToken = await _apiClient.GenerateClientTokenAsync(basiqUserId, ct);

        var consentUrl = $"https://consent.basiq.io/home?token={clientToken}";

        // Insert pending connection row
        var connection = await _dbExecutor.QuerySingleAsync<BasiqConnectionResponse>(
            new CommandDefinition(
                """
                INSERT INTO basiq_connections (
                    user_id, basiq_user_id, institution_name, status
                )
                VALUES (
                    @UserId, @BasiqUserId, @InstitutionName, 'pending'
                )
                RETURNING
                    id AS Id,
                    user_id AS UserId,
                    bank_account_id AS BankAccountId,
                    institution_name AS InstitutionName,
                    status AS Status,
                    sync_enabled AS SyncEnabled,
                    sync_frequency_minutes AS SyncFrequencyMinutes,
                    connected_at AS ConnectedAt,
                    last_sync_at AS LastSyncAt
                """,
                new
                {
                    UserId = userId,
                    BasiqUserId = basiqUserId,
                    InstitutionName = request.InstitutionName
                },
                cancellationToken: ct));

        _logger.LogInformation(
            "Basiq connection {ConnectionId} initiated for user {UserId}",
            connection.Id, userId);

        return new InitiateConnectionResponse
        {
            ConnectionId = connection.Id,
            ConsentUrl = consentUrl,
            InstitutionName = request.InstitutionName,
            Status = "pending"
        };
    }

    // ─── Complete Connection (after consent callback) ──────────────────────

    public async Task<BasiqConnectionResponse> CompleteConnectionAsync(
        Guid userId, CompleteConnectionRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Completing Basiq connection {ConnectionId} for user {UserId}, job {JobId}",
            request.ConnectionId, userId, request.JobId);

        // Verify connection belongs to user
        var connection = await _dbExecutor.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT id, basiq_user_id, status
                FROM basiq_connections
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = request.ConnectionId, UserId = userId },
                cancellationToken: ct));

        if (connection is null)
            throw new InvalidOperationException("Connection not found.");

        if (connection.status != "pending")
            throw new InvalidOperationException("Connection is not in pending state.");

        string basiqUserId = connection.basiq_user_id;

        // Poll job until complete (with timeout)
        var job = await PollJobWithTimeoutAsync(request.JobId, ct);

        if (job.attributes?.status != "success")
        {
            var error = job.steps?.FirstOrDefault(s => s.status == "failed")?.error
                        ?? "Job did not complete successfully.";

            await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE basiq_connections
                    SET status = 'failed', updated_at = NOW()
                    WHERE id = @Id
                    """,
                    new { Id = request.ConnectionId },
                    cancellationToken: ct));

            throw new InvalidOperationException($"Bank connection failed: {error}");
        }

        // Job succeeded — fetch accounts from Basiq
        var accountsResponse = await _apiClient.GetAccountsAsync(basiqUserId, ct);

        BasiqConnectionResponse? result = null;

        foreach (var basiqAccount in accountsResponse.data)
        {
            if (basiqAccount.attributes is null) continue;

            var institution = basiqAccount.attributes.institution;
            var accountName = basiqAccount.attributes.name;
            var accountNo = basiqAccount.attributes.accountNo;
            var currency = basiqAccount.attributes.currency;

            // Compute account mask (last 4 digits)
            var mask = accountNo?.Length >= 4
                ? accountNo[^4..]
                : accountNo;

            // Create bank_account if it doesn't already exist for this Basiq user
            var bankAccountId = await _dbExecutor.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(
                    """
                    SELECT ba.id
                    FROM bank_accounts ba
                    JOIN basiq_connections bc ON bc.bank_account_id = ba.id
                    WHERE bc.basiq_user_id = @BasiqUserId
                    AND ba.user_id = @UserId
                    AND ba.account_mask = @Mask
                    LIMIT 1
                    """,
                    new { BasiqUserId = basiqUserId, UserId = userId, Mask = mask },
                    cancellationToken: ct));

            if (bankAccountId is null)
            {
                bankAccountId = await _dbExecutor.QuerySingleAsync<Guid>(
                    new CommandDefinition(
                        """
                        INSERT INTO bank_accounts (user_id, bank_name, account_name, account_mask, currency)
                        VALUES (@UserId, @BankName, @AccountName, @Mask, @Currency)
                        RETURNING id
                        """,
                        new
                        {
                            UserId = userId,
                            BankName = institution,
                            AccountName = accountName,
                            Mask = mask,
                            Currency = currency
                        },
                        cancellationToken: ct));
            }

            // Update or create connection row for this account
            result = await _dbExecutor.QuerySingleAsync<BasiqConnectionResponse>(
                new CommandDefinition(
                    """
                    UPDATE basiq_connections
                    SET bank_account_id = @BankAccountId,
                        status = 'active',
                        connected_at = NOW(),
                        updated_at = NOW()
                    WHERE id = @Id AND user_id = @UserId
                    RETURNING
                        id AS Id,
                        user_id AS UserId,
                        bank_account_id AS BankAccountId,
                        institution_name AS InstitutionName,
                        status AS Status,
                        sync_enabled AS SyncEnabled,
                        sync_frequency_minutes AS SyncFrequencyMinutes,
                        connected_at AS ConnectedAt,
                        last_sync_at AS LastSyncAt
                    """,
                    new
                    {
                        Id = request.ConnectionId,
                        UserId = userId,
                        BankAccountId = bankAccountId.Value
                    },
                    cancellationToken: ct));
        }

        if (result is null)
            throw new InvalidOperationException("No accounts found from Basiq.");

        _logger.LogInformation(
            "Basiq connection {ConnectionId} completed for user {UserId}",
            request.ConnectionId, userId);

        return result;
    }

    // ─── List Connections ──────────────────────────────────────────────────

    public async Task<BasiqConnectionListResponse> GetConnectionsAsync(
        Guid userId, CancellationToken ct)
    {
        var connections = (await _dbExecutor.QueryAsync<BasiqConnectionResponse>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    user_id AS UserId,
                    bank_account_id AS BankAccountId,
                    institution_name AS InstitutionName,
                    status AS Status,
                    sync_enabled AS SyncEnabled,
                    sync_frequency_minutes AS SyncFrequencyMinutes,
                    connected_at AS ConnectedAt,
                    last_sync_at AS LastSyncAt
                FROM basiq_connections
                WHERE user_id = @UserId
                ORDER BY created_at DESC
                """,
                new { UserId = userId },
                cancellationToken: ct)))
            .AsList();

        return new BasiqConnectionListResponse { Connections = connections };
    }

    // ─── Get Single Connection ─────────────────────────────────────────────

    public async Task<BasiqConnectionResponse> GetConnectionAsync(
        Guid userId, Guid connectionId, CancellationToken ct)
    {
        var connection = await _dbExecutor.QuerySingleOrDefaultAsync<BasiqConnectionResponse>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    user_id AS UserId,
                    bank_account_id AS BankAccountId,
                    institution_name AS InstitutionName,
                    status AS Status,
                    sync_enabled AS SyncEnabled,
                    sync_frequency_minutes AS SyncFrequencyMinutes,
                    connected_at AS ConnectedAt,
                    last_sync_at AS LastSyncAt
                FROM basiq_connections
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = connectionId, UserId = userId },
                cancellationToken: ct));

        if (connection is null)
            throw new InvalidOperationException("Connection not found.");

        return connection;
    }

    // ─── Update Sync Config ────────────────────────────────────────────────

    public async Task<BasiqConnectionResponse> UpdateSyncConfigAsync(
        Guid userId, Guid connectionId, UpdateSyncConfigRequest request, CancellationToken ct)
    {
        var setClauses = new List<string>();
        var parameters = new Dictionary<string, object?>
        {
            { "Id", connectionId },
            { "UserId", userId }
        };

        if (request.SyncEnabled.HasValue)
        {
            setClauses.Add("sync_enabled = @SyncEnabled");
            parameters["SyncEnabled"] = request.SyncEnabled.Value;
        }

        if (request.SyncFrequencyMinutes.HasValue)
        {
            setClauses.Add("sync_frequency_minutes = @SyncFrequencyMinutes");
            parameters["SyncFrequencyMinutes"] = request.SyncFrequencyMinutes.Value;
        }

        if (setClauses.Count == 0)
            throw new InvalidOperationException("No settings to update.");

        setClauses.Add("updated_at = NOW()");

        var sql = $"""
                   UPDATE basiq_connections
                   SET {string.Join(", ", setClauses)}
                   WHERE id = @Id AND user_id = @UserId
                   RETURNING
                       id AS Id,
                       user_id AS UserId,
                       bank_account_id AS BankAccountId,
                       institution_name AS InstitutionName,
                       status AS Status,
                       sync_enabled AS SyncEnabled,
                       sync_frequency_minutes AS SyncFrequencyMinutes,
                       connected_at AS ConnectedAt,
                       last_sync_at AS LastSyncAt
                   """;

        var result = await _dbExecutor.QuerySingleOrDefaultAsync<BasiqConnectionResponse>(
            new CommandDefinition(
                sql,
                new DynamicParameters(parameters),
                cancellationToken: ct));

        if (result is null)
            throw new InvalidOperationException("Connection not found.");

        _logger.LogInformation(
            "Sync config updated for connection {ConnectionId}: enabled={Enabled}, freq={Freq}min",
            connectionId, request.SyncEnabled, request.SyncFrequencyMinutes);

        return result;
    }

    // ─── Refresh / Manual Sync ─────────────────────────────────────────────

    public async Task<BasiqConnectionResponse> RefreshConnectionAsync(
        Guid userId, Guid connectionId, CancellationToken ct)
    {
        _logger.LogInformation(
            "Manual sync triggered for connection {ConnectionId} by user {UserId}",
            connectionId, userId);

        var count = await SyncTransactionsAsync(userId, connectionId, ct);

        _logger.LogInformation(
            "Manual sync completed for connection {ConnectionId}: {Count} transactions",
            connectionId, count);

        return await GetConnectionAsync(userId, connectionId, ct);
    }

    // ─── Remove Connection ─────────────────────────────────────────────────

    public async Task RemoveConnectionAsync(
        Guid userId, Guid connectionId, CancellationToken ct)
    {
        var rows = await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM basiq_connections
                WHERE id = @Id AND user_id = @UserId
                """,
                new { Id = connectionId, UserId = userId },
                cancellationToken: ct));

        if (rows == 0)
            throw new InvalidOperationException("Connection not found.");

        _logger.LogInformation(
            "Basiq connection {ConnectionId} removed by user {UserId}",
            connectionId, userId);
    }

    // ─── Sync Log ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SyncLogResponse>> GetSyncLogAsync(
        Guid userId, Guid connectionId, int limit, CancellationToken ct)
    {
        // Verify connection belongs to user
        var belongsToUser = await _dbExecutor.QuerySingleOrDefaultAsync<bool>(
            new CommandDefinition(
                """
                SELECT EXISTS (
                    SELECT 1 FROM basiq_connections
                    WHERE id = @Id AND user_id = @UserId
                )
                """,
                new { Id = connectionId, UserId = userId },
                cancellationToken: ct));

        if (!belongsToUser)
            throw new InvalidOperationException("Connection not found.");

        return (await _dbExecutor.QueryAsync<SyncLogResponse>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    status AS Status,
                    transactions_fetched AS TransactionsFetched,
                    transactions_inserted AS TransactionsInserted,
                    error_message AS ErrorMessage,
                    synced_at AS SyncedAt
                FROM basiq_sync_log
                WHERE basiq_connection_id = @ConnectionId
                ORDER BY synced_at DESC
                LIMIT @Limit
                """,
                new { ConnectionId = connectionId, Limit = limit },
                cancellationToken: ct)))
            .AsList();
    }

    // ─── Core Sync Logic ──────────────────────────────────────────────────

    public async Task<int> SyncTransactionsAsync(
        Guid userId, Guid connectionId, CancellationToken ct)
    {
        // Get connection details
        var connection = await _dbExecutor.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition(
                """
                SELECT
                    bc.basiq_user_id,
                    bc.bank_account_id,
                    bc.institution_name,
                    bc.last_sync_at,
                    ba.currency
                FROM basiq_connections bc
                JOIN bank_accounts ba ON ba.id = bc.bank_account_id
                WHERE bc.id = @Id AND bc.user_id = @UserId AND bc.status = 'active'
                """,
                new { Id = connectionId, UserId = userId },
                cancellationToken: ct));

        if (connection is null)
            throw new InvalidOperationException(
                "Active connection not found or bank account missing.");

        string basiqUserId = connection.basiq_user_id;
        Guid bankAccountId = connection.bank_account_id;
        string institutionName = connection.institution_name;
        DateTime? lastSyncAt = connection.last_sync_at;

        // Determine 'since' date: start from last sync, or 90 days ago for first sync
        // Basiq expects ISO 8601 date format (yyyy-MM-dd)
        string? since = lastSyncAt.HasValue
            ? lastSyncAt.Value.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd");

        _logger.LogDebug(
            "Syncing transactions for connection {ConnectionId}, since={Since}",
            connectionId, since);

        // Fetch transactions from Basiq
        var transactions = await _apiClient.GetTransactionsAsync(basiqUserId, since, ct);

        if (transactions.Count == 0)
        {
            _logger.LogDebug("No new transactions found for connection {ConnectionId}", connectionId);
            return 0;
        }

        // Create synthetic bank_statement record
        var syncDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var statementId = await _dbExecutor.QuerySingleAsync<Guid>(
            new CommandDefinition(
                """
                INSERT INTO bank_statements (
                    user_id, bank_account_id, original_file_name, stored_file_name,
                    file_hash, content_type, size_in_bytes, status, source,
                    statement_start_date, statement_end_date, processed_at
                )
                VALUES (
                    @UserId, @BankAccountId, @OriginalFileName, @StoredFileName,
                    @FileHash, 'application/json', 0, 'processed', 'basiq',
                    @StartDate, @EndDate, NOW()
                )
                RETURNING id
                """,
                new
                {
                    UserId = userId,
                    BankAccountId = bankAccountId,
                    OriginalFileName = $"[Basiq] {institutionName}",
                    StoredFileName = $"basiq-synthetic-{Guid.NewGuid():N}",
                    FileHash = $"basiq-{connectionId:N}-{syncDate}",
                    StartDate = DateTime.UtcNow.AddDays(-90).Date,
                    EndDate = DateTime.UtcNow.Date
                },
                cancellationToken: ct));

        // Insert transactions with deduplication by external_reference
        var insertedCount = 0;

        foreach (var txn in transactions)
        {
            if (txn.attributes is null) continue;

            var attrs = txn.attributes;

            if (!DateOnly.TryParse(attrs.transactionDate, out var txnDate))
                continue;

            if (!decimal.TryParse(attrs.amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            var txnType = attrs.classification switch
            {
                "credit" => "credit",
                "debit" => "debit",
                _ => amount >= 0 ? "credit" : "debit"
            };

            decimal? balanceAfter = null;
            if (!string.IsNullOrEmpty(attrs.balance) &&
                decimal.TryParse(attrs.balance, NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
            {
                balanceAfter = bal;
            }

            var rows = await _dbExecutor.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO statement_transactions (
                        bank_statement_id, bank_account_id,
                        transaction_date, description, merchant_name,
                        amount, transaction_type, balance_after, external_reference
                    )
                    SELECT
                        @StatementId, @BankAccountId,
                        @TransactionDate, @Description, @MerchantName,
                        @Amount, @TransactionType, @BalanceAfter, @ExternalReference
                    WHERE NOT EXISTS (
                        SELECT 1 FROM statement_transactions st
                        WHERE st.bank_account_id = @BankAccountId
                        AND st.external_reference = @ExternalReference
                    )
                    """,
                    new
                    {
                        StatementId = statementId,
                        BankAccountId = bankAccountId,
                        TransactionDate = txnDate,
                        Description = attrs.description,
                        MerchantName = attrs.merchantName ?? (object)DBNull.Value,
                        Amount = Math.Abs(amount),
                        TransactionType = txnType,
                        BalanceAfter = balanceAfter.HasValue ? (object)balanceAfter.Value : DBNull.Value,
                        ExternalReference = txn.id
                    },
                    cancellationToken: ct));

            insertedCount += rows;
        }

        // Update last_sync_at
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE basiq_connections
                SET last_sync_at = NOW(), updated_at = NOW()
                WHERE id = @Id
                """,
                new { Id = connectionId },
                cancellationToken: ct));

        // Insert sync log
        await _dbExecutor.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO basiq_sync_log (
                    basiq_connection_id, status, transactions_fetched,
                    transactions_inserted, bank_statement_id
                )
                VALUES (
                    @ConnectionId, 'success', @Fetched,
                    @Inserted, @StatementId
                )
                """,
                new
                {
                    ConnectionId = connectionId,
                    Fetched = transactions.Count,
                    Inserted = insertedCount,
                    StatementId = statementId
                },
                cancellationToken: ct));

        _logger.LogInformation(
            "Basiq sync for connection {ConnectionId}: {Fetched} fetched, {Inserted} new",
            connectionId, transactions.Count, insertedCount);

        // Send SignalR notification
        try
        {
            await _hubContext.Clients.User(userId.ToString()).SendAsync(
                "BasiqSyncCompleted",
                new
                {
                    connectionId,
                    transactionsFetched = transactions.Count,
                    transactionsInserted = insertedCount,
                    statementId
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR notification for Basiq sync");
        }

        return insertedCount;
    }

    // ─── Private Helpers ───────────────────────────────────────────────────

    private async Task<string> GetOrCreateBasiqUserIdAsync(
        Guid userId, string email, CancellationToken ct)
    {
        // Check if user already has a Basiq user ID
        var existingBasiqId = await _dbExecutor.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                """
                SELECT basiq_user_id
                FROM basiq_connections
                WHERE user_id = @UserId
                LIMIT 1
                """,
                new { UserId = userId },
                cancellationToken: ct));

        if (!string.IsNullOrEmpty(existingBasiqId))
            return existingBasiqId;

        // Create a new Basiq user
        return await _apiClient.CreateUserAsync(email, ct);
    }

    private async Task<BasiqJobResponse> PollJobWithTimeoutAsync(
        string jobId, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(120);
        var pollInterval = TimeSpan.FromSeconds(2);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var job = await _apiClient.GetJobAsync(jobId, ct);

            if (job.attributes?.status is "success" or "failed")
            {
                _logger.LogDebug(
                    "Job {JobId} completed with status: {Status}",
                    jobId, job.attributes.status);
                return job;
            }

            await Task.Delay(pollInterval, ct);
        }

        throw new InvalidOperationException(
            $"Job {jobId} did not complete within the timeout period.");
    }
}
