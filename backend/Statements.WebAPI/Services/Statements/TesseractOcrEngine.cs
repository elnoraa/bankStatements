using System.Diagnostics;

namespace Statements.WebAPI.Services.Statements;

/// <summary>
/// OCR engine that invokes the Tesseract CLI to extract text from image-based PDFs.
/// Requires tesseract-ocr to be installed on the system.
/// </summary>
public sealed class TesseractOcrEngine : IOCREngine
{
    private readonly ILogger<TesseractOcrEngine> _logger;

    public TesseractOcrEngine(ILogger<TesseractOcrEngine> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OcrResult> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting OCR fallback for file: {FilePath}", filePath);

        try
        {
            var tempOutput = Path.GetTempFileName();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tesseract",
                    Arguments = $"\"{filePath}\" \"{tempOutput}\" -l eng 2>&1",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                // Read stderr separately to avoid deadlocks
                var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("Tesseract exited with code {ExitCode}: {Error}", process.ExitCode, errorOutput);
                    return new OcrResult(null, true);
                }

                var outputFile = tempOutput + ".txt";
                if (!File.Exists(outputFile))
                {
                    _logger.LogWarning("Tesseract did not produce output file: {Output}", outputFile);
                    return new OcrResult(null, true);
                }

                var text = await File.ReadAllTextAsync(outputFile, cancellationToken);
                File.Delete(outputFile);

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Tesseract produced empty text for: {FilePath}", filePath);
                    return new OcrResult(null, true);
                }

                _logger.LogInformation("OCR fallback succeeded for {FilePath} ({Length} chars)", filePath, text.Length);
                return new OcrResult(text, true);
            }
            finally
            {
                if (File.Exists(tempOutput)) File.Delete(tempOutput);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OCR fallback failed for file: {FilePath}", filePath);
            return new OcrResult(null, true);
        }
    }
}
