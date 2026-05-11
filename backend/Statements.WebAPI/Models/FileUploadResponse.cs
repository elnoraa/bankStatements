namespace Statements.WebAPI.Models;

public record FileUploadResponse(
    string FileName,
    string StoredFileName,
    long SizeInBytes,
    string ContentType,
    string SavedPath);
