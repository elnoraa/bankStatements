using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Messaging;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Services.Statements;

/// <summary>
/// Unit tests for <see cref="StatementService"/>.
/// </summary>
public sealed class StatementServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly Mock<IVirusScanService> _virusScanServiceMock = new();
    private readonly Mock<IMessagePublisher> _messagePublisherMock = new();
    private readonly Mock<IWebHostEnvironment> _environmentMock = new();
    private readonly IConfiguration _configuration;
    private readonly StatementService _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _bankAccountId = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the <see cref="StatementServiceTests"/> class.
    /// Sets up in-memory configuration and mocks.
    /// </summary>
    public StatementServiceTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:UploadsDirectory"] = Path.GetTempPath()
            })
            .Build();

        _environmentMock.Setup(x => x.ContentRootPath).Returns(Directory.GetCurrentDirectory());

        _sut = new StatementService(
            _dbExecutorMock.Object,
            _virusScanServiceMock.Object,
            _messagePublisherMock.Object,
            _environmentMock.Object,
            _configuration,
            Mock.Of<ILogger<StatementService>>());
    }

    /// <summary>
    /// Creates a mock PDF form file for testing.
    /// </summary>
    /// <param name="fileName">The file name (default: "statement.pdf").</param>
    /// <param name="size">The file size in bytes (default: 1024).</param>
    /// <returns>A configured mock <see cref="IFormFile"/>.</returns>
    private static Mock<IFormFile> CreateMockPdfFile(string fileName = "statement.pdf", long size = 1024)
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(x => x.FileName).Returns(fileName);
        fileMock.Setup(x => x.Length).Returns(size);
        fileMock.Setup(x => x.ContentType).Returns("application/pdf");
        fileMock.Setup(x => x.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((stream, _) =>
            {
                // Write some data so the SHA256 hash gets computed
                var data = "fake pdf content"u8.ToArray();
                stream.Write(data, 0, data.Length);
            })
            .Returns(Task.CompletedTask);
        return fileMock;
    }

    /// <summary>
    /// Verifies that uploading a non-PDF file throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public async Task UploadAsync_WithNonPdfFile_ThrowsInvalidOperationException()
    {
        var fileMock = CreateMockPdfFile("statement.txt");

        var act = () => _sut.UploadAsync(_userId, _bankAccountId, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Only PDF bank statements are supported.");
    }

    /// <summary>
    /// Verifies that uploading with an invalid bank account ID throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public async Task UploadAsync_WithInvalidBankAccount_ThrowsInvalidOperationException()
    {
        var fileMock = CreateMockPdfFile();
        var bankAccountId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<bool>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(false);

        var act = () => _sut.UploadAsync(_userId, bankAccountId, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The selected bank account does not exist for this user.");
    }

    /// <summary>
    /// Verifies that uploading a file detected as infected throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public async Task UploadAsync_WithVirusDetected_ThrowsInvalidOperationException()
    {
        var fileMock = CreateMockPdfFile();
        var bankAccountId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<bool>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(true);

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(false, "EICAR-Test-File", TimeSpan.FromMilliseconds(100)));

        var act = () => _sut.UploadAsync(_userId, bankAccountId, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*virus*");
    }

    /// <summary>
    /// Verifies that a virus scan error throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public async Task UploadAsync_WithVirusScanError_ThrowsInvalidOperationException()
    {
        var fileMock = CreateMockPdfFile();

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<bool>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(true);

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(false, null, TimeSpan.FromMilliseconds(100)));

        var act = () => _sut.UploadAsync(_userId, _bankAccountId, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be verified as safe*");
    }

    /// <summary>
    /// Verifies that a clean file upload saves the file, inserts the record,
    /// publishes a message, and returns a response with status "uploaded".
    /// </summary>
    [Fact]
    public async Task UploadAsync_WithCleanFileAndNewStatement_SavesAndPublishesMessage()
    {
        var fileMock = CreateMockPdfFile();
        var statementId = Guid.NewGuid();

        // Bank account ownership check passes
        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<bool>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(true);

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(true, null, TimeSpan.FromMilliseconds(100)));

        // No duplicate found
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid?)null);

        // Insert statement returns response
        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<StatementUploadResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new StatementUploadResponse
            {
                Id = statementId,
                UserId = _userId,
                OriginalFileName = "statement.pdf",
                StoredFileName = "statement-file.pdf",
                FileHash = "HASH123",
                SizeInBytes = 1024,
                ContentType = "application/pdf",
                Status = "uploaded",
                UploadedAt = DateTimeOffset.UtcNow
            });

        // Publish endpoint mock (no setup needed — we just verify it was called)

        var result = await _sut.UploadAsync(_userId, _bankAccountId, fileMock.Object, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be("uploaded");
        result.Id.Should().Be(statementId);

        // Verify a ProcessStatementMessage was published
        _messagePublisherMock.Verify(
            x => x.PublishAsync(It.IsAny<ProcessStatementMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that uploading a file with a duplicate hash returns the existing statement without re-processing.
    /// </summary>
    [Fact]
    public async Task UploadAsync_WithDuplicateHash_ReturnsExistingStatement()
    {
        var fileMock = CreateMockPdfFile();
        var existingId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<bool>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(true);

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(true, null, TimeSpan.FromMilliseconds(100)));

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(existingId);

        var result = await _sut.UploadAsync(_userId, _bankAccountId, fileMock.Object, CancellationToken.None);

        result.Should().NotBeNull();
        result.Id.Should().Be(existingId);
        result.Status.Should().Be("uploaded");
        result.ParsedTransactionCount.Should().Be(0);

        // Message should not be published for a duplicate
        _messagePublisherMock.Verify(
            x => x.PublishAsync(It.IsAny<ProcessStatementMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that GetStatementAsync returns the statement when it exists and belongs to the user.
    /// </summary>
    [Fact]
    public async Task GetStatementAsync_WithValidId_ReturnsStatement()
    {
        var statementId = Guid.NewGuid();

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<StatementUploadResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new StatementUploadResponse
            {
                Id = statementId,
                UserId = _userId,
                OriginalFileName = "statement.pdf",
                StoredFileName = "statement-file.pdf",
                FileHash = "HASH123",
                SizeInBytes = 1024,
                Status = "processing",
                UploadedAt = DateTimeOffset.UtcNow
            });

        var result = await _sut.GetStatementAsync(_userId, statementId, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(statementId);
        result.Status.Should().Be("processing");
    }

    /// <summary>
    /// Verifies that GetStatementAsync returns null when the statement does not belong to the user.
    /// </summary>
    [Fact]
    public async Task GetStatementAsync_WithNonExistentId_ReturnsNull()
    {
        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<StatementUploadResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((StatementUploadResponse?)null);

        var result = await _sut.GetStatementAsync(_userId, Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }
}
