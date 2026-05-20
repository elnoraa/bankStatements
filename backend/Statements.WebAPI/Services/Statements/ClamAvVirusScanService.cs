using nClam;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Options for the ClamAV virus scanner connection.
/// </summary>
public sealed class ClamAvOptions
{
    public const string SectionName = "ClamAv";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 3310;
    public int ScanTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Virus scanning service that delegates to a ClamAV daemon (clamd) via TCP.
/// </summary>
public sealed class ClamAvVirusScanService : IVirusScanService, IDisposable
{
    private readonly ClamClient _clamClient;
    private readonly int _scanTimeoutSeconds;
    private readonly ILogger<ClamAvVirusScanService> _logger;
    private bool _disposed;

    public ClamAvVirusScanService(
        IConfiguration configuration,
        ILogger<ClamAvVirusScanService> logger)
    {
        _logger = logger;

        var options = configuration
            .GetSection(ClamAvOptions.SectionName)
            .Get<ClamAvOptions>() ?? new ClamAvOptions();

        _scanTimeoutSeconds = options.ScanTimeoutSeconds;
        _clamClient = new ClamClient(options.Host, options.Port)
        {
            MaxStreamSize = 25 * 1024 * 1024 // 25 MB — above the 10 MB upload limit
        };

        _logger.LogInformation(
            "ClamAV scanner configured: {Host}:{Port}, timeout={Timeout}s",
            options.Host, options.Port, _scanTimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<VirusScanResult> ScanAsync(string filePath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting virus scan: {FilePath}", filePath);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            scanCts.CancelAfter(TimeSpan.FromSeconds(_scanTimeoutSeconds));

            // Retry file read for transient Docker overlay2 fs inconsistencies
            byte[] fileBytes;
            const int maxRetries = 3;
            const int retryDelayMs = 100;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    fileBytes = await File.ReadAllBytesAsync(filePath, scanCts.Token);
                    break;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    _logger.LogDebug(
                        "Retrying file read for virus scan (attempt {Attempt}/{MaxRetries}): {FilePath}",
                        attempt, maxRetries, filePath);
                    await Task.Delay(retryDelayMs, scanCts.Token);
                }
            }

            var result = await _clamClient.SendAndScanFileAsync(fileBytes, scanCts.Token);

            sw.Stop();

            switch (result.Result)
            {
                case ClamScanResults.VirusDetected:
                    var virusName = result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown";
                    _logger.LogWarning(
                        "Virus scan FAILED: {FilePath} — {VirusName} detected ({Duration}ms)",
                        filePath, virusName, sw.ElapsedMilliseconds);
                    return new VirusScanResult(IsClean: false, VirusName: virusName, sw.Elapsed);

                case ClamScanResults.Error:
                    _logger.LogError(
                        "Virus scan ERROR: {FilePath} — {ErrorMessage} ({Duration}ms)",
                        filePath, result.RawResult, sw.ElapsedMilliseconds);
                    return new VirusScanResult(IsClean: false, VirusName: null, sw.Elapsed);

                default:
                    _logger.LogInformation(
                        "Virus scan PASSED: {FilePath} ({Duration}ms)",
                        filePath, sw.ElapsedMilliseconds);
                    return new VirusScanResult(IsClean: true, VirusName: null, sw.Elapsed);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogError(
                "Virus scan TIMEOUT: {FilePath} after {Duration}ms (limit={Timeout}s)",
                filePath, sw.ElapsedMilliseconds, _scanTimeoutSeconds);
            return new VirusScanResult(IsClean: false, VirusName: null, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Virus scan FAILED with exception: {FilePath} ({Duration}ms)",
                filePath, sw.ElapsedMilliseconds);
            return new VirusScanResult(IsClean: false, VirusName: null, sw.Elapsed);
        }
    }

    /// <summary>
    /// Disposes the underlying <see cref="ClamClient"/> instance.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            (_clamClient as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
