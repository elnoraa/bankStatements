namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// Result of an OCR extraction attempt.
/// </summary>
/// <param name="Text">The extracted text content, or null if OCR failed.</param>
/// <param name="UsedOcr">Whether OCR was used (as opposed to direct text extraction).</param>
public sealed record OcrResult(string? Text, bool UsedOcr);

/// <summary>
/// Optical character recognition engine for extracting text from scanned/image-based PDFs.
/// </summary>
public interface IOCREngine
{
    /// <summary>
    /// Attempts to extract text from the given PDF file using OCR.
    /// </summary>
    /// <param name="filePath">Path to the PDF file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OcrResult"/> with extracted text, or null if OCR failed.</returns>
    Task<OcrResult> ExtractTextAsync(string filePath, CancellationToken cancellationToken);
}
