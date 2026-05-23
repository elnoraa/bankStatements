namespace Statements.WebAPI.Services.Basiq;

// ─── Token ────────────────────────────────────────────────────────────────

public sealed record BasiqTokenResponse
{
    public string access_token { get; init; } = string.Empty;
    public string token_type { get; init; } = string.Empty;
    public int expires_in { get; init; }
}

// ─── User ─────────────────────────────────────────────────────────────────

public sealed record BasiqUserResponse
{
    public string type { get; init; } = string.Empty;
    public string id { get; init; } = string.Empty;
}

// ─── Job (consent flow) ────────────────────────────────────────────────────

public sealed record BasiqJobResponse
{
    public string type { get; init; } = string.Empty;
    public string id { get; init; } = string.Empty;
    public BasiqJobAttributes? attributes { get; init; }
    public List<BasiqJobStep>? steps { get; init; }
}

public sealed record BasiqJobAttributes
{
    public string status { get; init; } = string.Empty; // "pending" | "success" | "failed"
    public DateTime? createdDate { get; init; }
    public DateTime? updatedDate { get; init; }
}

public sealed record BasiqJobStep
{
    public string title { get; init; } = string.Empty;  // "verify-credentials", "retrieve-accounts", "retrieve-transactions"
    public string status { get; init; } = string.Empty; // "pending" | "success" | "failed"
    public string? result { get; init; }
    public string? error { get; init; }
}

// ─── List envelope ─────────────────────────────────────────────────────────

public sealed record BasiqListResponse<T>
{
    public string type { get; init; } = string.Empty;
    public List<T> data { get; init; } = new();
    public BasiqPaginationLinks? links { get; init; }
}

public sealed record BasiqPaginationLinks
{
    public string? self { get; init; }
    public string? next { get; init; }
}

// ─── Accounts ──────────────────────────────────────────────────────────────

public sealed record BasiqAccountApiResponse
{
    public string type { get; init; } = string.Empty;
    public string id { get; init; } = string.Empty;
    public BasiqAccountAttributes? attributes { get; init; }
}

public sealed record BasiqAccountAttributes
{
    public string accountNo { get; init; } = string.Empty;
    public string name { get; init; } = string.Empty;
    public string currency { get; init; } = string.Empty;
    public string institution { get; init; } = string.Empty;
    public string? classType { get; init; }
    public decimal? balance { get; init; }
}

// ─── Transactions ──────────────────────────────────────────────────────────

public sealed record BasiqTransactionApiResponse
{
    public string type { get; init; } = string.Empty;
    public string id { get; init; } = string.Empty;
    public BasiqTransactionAttributes? attributes { get; init; }
}

public sealed record BasiqTransactionAttributes
{
    public string status { get; init; } = string.Empty;
    public string description { get; init; } = string.Empty;
    public string? merchantName { get; init; }
    public string amount { get; init; } = string.Empty;
    public string? balance { get; init; }
    public string currency { get; init; } = string.Empty;
    public string transactionDate { get; init; } = string.Empty;
    public string? postDate { get; init; }
    public string? classification { get; init; } // "debit" | "credit" | null
    public string? institution { get; init; }
}
