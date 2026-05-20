using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Statements;
using Statements.WebAPI.Data;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Services.Statements;

public sealed class StatementServiceTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly Mock<IStatementParser> _statementParserMock = new();
    private readonly Mock<IVirusScanService> _virusScanServiceMock = new();
    private readonly Mock<IWebHostEnvironment> _environmentMock = new();
    private readonly IConfiguration _configuration;
    private readonly StatementService _sut;
    private readonly Guid _userId = Guid.NewGuid();

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
            _statementParserMock.Object,
            _virusScanServiceMock.Object,
            _environmentMock.Object,
            _configuration,
            Mock.Of<ILogger<StatementService>>());
    }

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

    [Fact]
    public async Task UploadAsync_WithNonPdfFile_ThrowsInvalidOperationException()
    {
        var fileMock = CreateMockPdfFile("statement.txt");

        var act = () => _sut.UploadAsync(_userId, null, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Only PDF bank statements are supported.");
    }

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

    [Fact]
    public async Task UploadAsync_WithVirusScanError_ThrowsInvalidOperationException()
    {
        var fileMock = CreateMockPdfFile();

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(false, null, TimeSpan.FromMilliseconds(100)));

        var act = () => _sut.UploadAsync(_userId, null, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be verified as safe*");
    }

    [Fact]
    public async Task UploadAsync_WithCleanFileAndNewStatement_SavesAndReturnsResponse()
    {
        var fileMock = CreateMockPdfFile();
        var statementId = Guid.NewGuid();

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

        _statementParserMock
            .Setup(x => x.Parse(It.IsAny<string>()))
            .Returns(new List<ParsedStatementTransaction>
            {
                new(new DateOnly(2025, 1, 15), "Coles", 50.00m, "debit", null, "Groceries")
            });

        // ExecuteAsync for transaction inserts + status update
        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        // Second QuerySingleAsync for the processed statement
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
                Status = "processed",
                UploadedAt = DateTimeOffset.UtcNow,
                ParsedTransactionCount = 1
            });

        var result = await _sut.UploadAsync(_userId, null, fileMock.Object, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be("processed");
        result.Id.Should().Be(statementId);
    }

    [Fact]
    public async Task UploadAsync_WithDuplicateHash_ReturnsExistingStatement()
    {
        var fileMock = CreateMockPdfFile();
        var existingId = Guid.NewGuid();

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(true, null, TimeSpan.FromMilliseconds(100)));

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(existingId);

        var result = await _sut.UploadAsync(_userId, null, fileMock.Object, CancellationToken.None);

        result.Should().NotBeNull();
        result.Id.Should().Be(existingId);
        result.Status.Should().Be("uploaded");
        result.ParsedTransactionCount.Should().Be(0);
        _statementParserMock.Verify(x => x.Parse(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadAsync_WhenParserThrows_SetsStatusToFailed()
    {
        var fileMock = CreateMockPdfFile();

        _virusScanServiceMock
            .Setup(x => x.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VirusScanResult(true, null, TimeSpan.FromMilliseconds(100)));

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<Guid?>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync((Guid?)null);

        _dbExecutorMock
            .Setup(x => x.QuerySingleAsync<StatementUploadResponse>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(new StatementUploadResponse
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                OriginalFileName = "statement.pdf",
                StoredFileName = "statement-file.pdf",
                FileHash = "HASH123",
                SizeInBytes = 1024,
                ContentType = "application/pdf",
                Status = "uploaded",
                UploadedAt = DateTimeOffset.UtcNow
            });

        _statementParserMock
            .Setup(x => x.Parse(It.IsAny<string>()))
            .Throws(new InvalidDataException("Corrupt PDF"));

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        var act = () => _sut.UploadAsync(_userId, null, fileMock.Object, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*corrupted*");
    }
}
