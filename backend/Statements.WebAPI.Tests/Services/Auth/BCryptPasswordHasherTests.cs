using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Auth;

namespace Statements.WebAPI.Tests.Services.Auth;

public sealed class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut;

    public BCryptPasswordHasherTests()
    {
        var logger = Mock.Of<ILogger<BCryptPasswordHasher>>();
        _sut = new BCryptPasswordHasher(logger);
    }

    [Fact]
    public void Hash_ReturnsNonEmptyString_DifferentFromInput()
    {
        var password = "MySecureP@ss1";

        var hash = _sut.Hash(password);

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(password);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes_DueToSalt()
    {
        var password = "MySecureP@ss1";

        var hash1 = _sut.Hash(password);
        var hash2 = _sut.Hash(password);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var password = "MySecureP@ss1";
        var hash = _sut.Hash(password);

        var result = _sut.Verify(password, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WithIncorrectPassword_ReturnsFalse()
    {
        var password = "MySecureP@ss1";
        var hash = _sut.Hash(password);

        var result = _sut.Verify("WrongPassword", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var password = "MySecureP@ss1";
        var hash = _sut.Hash(password);

        var result = _sut.Verify("mysecurep@ss1", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Hash_Output_IsValidBCryptHash()
    {
        var password = "MySecureP@ss1";

        var hash = _sut.Hash(password);

        // BCrypt hashes start with $2a$, $2b$, or $2y$
        hash.Should().MatchRegex("^\\$2[aby]\\$\\d{2}\\$");
    }
}
