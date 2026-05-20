namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Result of a virus scan operation on a file.
/// </summary>
public sealed record VirusScanResult(
    bool IsClean,
    string? VirusName,
    TimeSpan Duration);

/// <summary>
/// Provides virus scanning capabilities for uploaded files.
/// </summary>
public interface IVirusScanService
{
    /// <summary>
    /// Scans the specified file for viruses.
    /// </summary>
    /// <param name="filePath">The absolute path to the file to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="VirusScanResult"/> indicating whether the file is clean.</returns>
    Task<VirusScanResult> ScanAsync(string filePath, CancellationToken cancellationToken);
}
