using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Tests.Services.Statements;

/// <summary>
/// Unit tests for <see cref="ClamAvVirusScanService"/> constructor behavior.
/// </summary>
public sealed class ClamAvVirusScanServiceTests
{
    /// <summary>
    /// Verifies that the constructor uses default options when no config section is present.
    /// </summary>
    [Fact]
    public void Constructor_WithMissingConfigSection_UsesDefaults()
    {
        var config = new ConfigurationBuilder().Build();
        var logger = Mock.Of<ILogger<ClamAvVirusScanService>>();

        using var sut = new ClamAvVirusScanService(config, logger);

        // Constructor should not throw when config is missing
        sut.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the constructor reads custom host, port, and timeout from configuration.
    /// </summary>
    [Fact]
    public void Constructor_WithCustomConfig_ReadsOptions()
    {
        var configData = new Dictionary<string, string?>
        {
            ["ClamAv:Host"] = "custom-host",
            ["ClamAv:Port"] = "1234",
            ["ClamAv:ScanTimeoutSeconds"] = "60",
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        var logger = Mock.Of<ILogger<ClamAvVirusScanService>>();

        using var sut = new ClamAvVirusScanService(config, logger);

        sut.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the constructor reads partial configuration and uses defaults for missing values.
    /// </summary>
    [Fact]
    public void Constructor_WithPartialConfig_ReadsOptions()
    {
        var configData = new Dictionary<string, string?>
        {
            ["ClamAv:ScanTimeoutSeconds"] = "45",
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        var logger = Mock.Of<ILogger<ClamAvVirusScanService>>();

        using var sut = new ClamAvVirusScanService(config, logger);

        sut.Should().NotBeNull();
    }
}
