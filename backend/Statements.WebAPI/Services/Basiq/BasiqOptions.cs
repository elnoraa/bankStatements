namespace Statements.WebAPI.Services.Basiq;

public sealed class BasiqOptions
{
    public const string SectionName = "Basiq";

    public string ApiBaseUrl { get; init; } = "https://au-api.basiq.io";
    public string ApiKey { get; init; } = string.Empty;
    public string ConsentRedirectUrl { get; init; } = string.Empty;
    public int TokenCacheMinutes { get; init; } = 50;
    public int SyncCheckIntervalMinutes { get; init; } = 5;
}
