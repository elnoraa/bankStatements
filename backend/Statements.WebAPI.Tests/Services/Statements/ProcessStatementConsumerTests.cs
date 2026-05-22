using System.Dynamic;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Data;
using Statements.WebAPI.Hubs;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Services.Statements;

/// <summary>
/// Unit tests for <see cref="ProcessStatementConsumer"/>.
/// </summary>
public sealed class ProcessStatementConsumerTests
{
    private readonly Mock<IDbExecutor> _dbExecutorMock = new();
    private readonly Mock<IStatementParser> _parserMock = new();
    private readonly Mock<IOCREngine> _ocrEngineMock = new();
    private readonly IConfiguration _configuration;
    private readonly Mock<IHubContext<StatementProcessingHub>> _hubContextMock = new();
    private readonly Mock<ILogger<ProcessStatementConsumer>> _loggerMock = new();
    private readonly ProcessStatementConsumer _sut;

    public ProcessStatementConsumerTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:UploadsDirectory"] = Path.GetTempPath()
            })
            .Build();

        // OCR engine defaults to returning null text (no OCR result)
        _ocrEngineMock
            .Setup(x => x.ExtractTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult(null, false));

        _sut = new ProcessStatementConsumer(
            _dbExecutorMock.Object,
            _parserMock.Object,
            _ocrEngineMock.Object,
            _configuration,
            _hubContextMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Creates an ExpandoObject for mocking Dapper's dynamic queries.
    /// </summary>
    private static dynamic CreateDynamicRow(params (string Key, object Value)[] properties)
    {
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;
        foreach (var (key, value) in properties)
        {
            dict[key] = value;
        }
        return expando;
    }

    /// <summary>
    /// Sets up QuerySingleAsync&lt;dynamic&gt; to return a row with the given properties.
    /// </summary>
    private static void ReturnsDynamicRow(Mock<IDbExecutor> mock, params (string Key, object Value)[] properties)
    {
        var row = CreateDynamicRow(properties);
        mock.Setup(x => x.QuerySingleAsync<dynamic>(It.IsAny<CommandDefinition>()))
            .Returns(Task.FromResult<dynamic>(row));
    }

    private static ProcessStatementMessage CreateMessage(Guid? bankAccountId = null)
    {
        return new ProcessStatementMessage
        {
            StatementId = Guid.NewGuid(),
            StoredFileName = "test-statement.pdf",
            UserId = Guid.NewGuid(),
            BankAccountId = bankAccountId ?? Guid.NewGuid()
        };
    }

    [Fact]
    public async Task ConsumeAsync_WithUploadedStatus_ProcessesSuccessfully()
    {
        var bankAccountId = Guid.NewGuid();
        var message = CreateMessage(bankAccountId);

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync("uploaded");

        ReturnsDynamicRow(
            _dbExecutorMock,
            ("stored_file_name", "test-statement.pdf"),
            ("bank_account_id", bankAccountId));

        _parserMock
            .Setup(x => x.Parse(It.IsAny<string>()))
            .Returns(new List<ParsedStatementTransaction>
            {
                new(new DateOnly(2025, 1, 15), "Coles", 50.00m, "debit", null, "Groceries")
            });

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.ConsumeAsync(message, CancellationToken.None);

        _parserMock.Verify(x => x.Parse(It.IsAny<string>()), Times.Once);
        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("status = 'processed'"))), Times.Once);
    }

    [Fact]
    public async Task ConsumeAsync_WithAlreadyProcessedStatus_IsIdempotent()
    {
        var message = CreateMessage();

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync("processed");

        await _sut.ConsumeAsync(message, CancellationToken.None);

        _parserMock.Verify(x => x.Parse(It.IsAny<string>()), Times.Never);
        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_WithAlreadyFailedStatus_IsIdempotent()
    {
        var message = CreateMessage();

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync("failed");

        await _sut.ConsumeAsync(message, CancellationToken.None);

        _parserMock.Verify(x => x.Parse(It.IsAny<string>()), Times.Never);
        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_WhenParserThrows_MarksFailedAndRethrows()
    {
        var bankAccountId = Guid.NewGuid();
        var message = CreateMessage(bankAccountId);

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync("uploaded");

        _dbExecutorMock
            .SetupSequence(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        ReturnsDynamicRow(
            _dbExecutorMock,
            ("stored_file_name", "test-statement.pdf"),
            ("bank_account_id", bankAccountId));

        _parserMock
            .Setup(x => x.Parse(It.IsAny<string>()))
            .Throws(new InvalidDataException("Corrupt PDF"));

        var act = () => _sut.ConsumeAsync(message, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("status = 'failed'"))), Times.Once);
    }

    [Fact]
    public async Task ConsumeAsync_WhenInsertionFails_MarksFailedAndRethrows()
    {
        var bankAccountId = Guid.NewGuid();
        var message = CreateMessage(bankAccountId);

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync("uploaded");

        _dbExecutorMock
            .SetupSequence(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        ReturnsDynamicRow(
            _dbExecutorMock,
            ("stored_file_name", "test-statement.pdf"),
            ("bank_account_id", bankAccountId));

        _parserMock
            .Setup(x => x.Parse(It.IsAny<string>()))
            .Returns(new List<ParsedStatementTransaction>
            {
                new(new DateOnly(2025, 1, 15), "Coles", 50.00m, "debit", null, "Groceries")
            });

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
                c.CommandText.Contains("INSERT INTO statement_transactions"))))
            .ThrowsAsync(new InvalidOperationException("DB constraint violation"));

        var act = () => _sut.ConsumeAsync(message, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("status = 'failed'"))), Times.Once);
    }

    [Fact]
    public async Task ConsumeAsync_WithNoTransactions_StillMarksProcessed()
    {
        var bankAccountId = Guid.NewGuid();
        var message = CreateMessage(bankAccountId);

        _dbExecutorMock
            .Setup(x => x.QuerySingleOrDefaultAsync<string>(It.IsAny<CommandDefinition>()))
            .ReturnsAsync("uploaded");

        _dbExecutorMock
            .SetupSequence(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        ReturnsDynamicRow(
            _dbExecutorMock,
            ("stored_file_name", "test-statement.pdf"),
            ("bank_account_id", bankAccountId));

        _parserMock
            .Setup(x => x.Parse(It.IsAny<string>()))
            .Returns(new List<ParsedStatementTransaction>());

        _dbExecutorMock
            .Setup(x => x.ExecuteAsync(It.IsAny<CommandDefinition>()))
            .ReturnsAsync(1);

        await _sut.ConsumeAsync(message, CancellationToken.None);

        _dbExecutorMock.Verify(x => x.ExecuteAsync(It.Is<CommandDefinition>(c =>
            c.CommandText.Contains("status = 'processed'"))), Times.Once);
    }
}
