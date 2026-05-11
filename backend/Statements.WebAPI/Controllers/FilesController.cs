using Microsoft.AspNetCore.Mvc;
using Statements.WebAPI.Models;

namespace Statements.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public FilesController(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<FileUploadResponse>> Upload([FromForm] IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest("Upload a non-empty file.");
        }

        var uploadsDirectory = GetUploadsDirectory();
        Directory.CreateDirectory(uploadsDirectory);

        var originalFileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(originalFileName);
        var storedFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}-{Guid.NewGuid():N}{extension}";
        var savedPath = Path.Combine(uploadsDirectory, storedFileName);

        await using var stream = System.IO.File.Create(savedPath);
        await file.CopyToAsync(stream);

        var response = new FileUploadResponse(
            originalFileName,
            storedFileName,
            file.Length,
            file.ContentType,
            savedPath);

        return Created($"/api/files/{storedFileName}", response);
    }

    private string GetUploadsDirectory()
    {
        var configuredDirectory = _configuration["FileStorage:UploadsDirectory"];

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        var backendDirectory = Directory.GetParent(_environment.ContentRootPath)?.FullName
            ?? _environment.ContentRootPath;

        return Path.Combine(backendDirectory, "Uploads");
    }
}
