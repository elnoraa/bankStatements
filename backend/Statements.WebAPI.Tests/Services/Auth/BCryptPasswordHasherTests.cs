using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Statements.WebAPI.Auth;

namespace Statements.WebAPI.Tests.Services.Auth;

/// <summary>
/// Unit tests for <see cref="BCryptPasswordHasher"/>.
/// </summary>
public sealed class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="BCryptPasswordHasherTests"/> class.
    /// </summary>
    public BCryptPasswordHasherTests()
    {
        var logger = Mock.Of<ILogger<BCryptPasswordHasher>>();
        _sut = new BCryptPasswordHasher(logger);
    }

    /// <summary>
    /// Verifies that the hash output is a non-empty string different from the input password.
    /// </summary>
    [Fact]
    public void Hash_ReturnsNonEmptyString_DifferentFromInput()
    {
        var password = "MySecureP@ss1";

        var hash = _sut.Hash(password);

        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(password);
    }

    /// <summary>
    /// Verifies that hashing the same password twice produces different results (due to unique salt).
    /// </summary>
    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes_DueToSalt()
    {
        var password = "MySecureP@ss1";

        var hash1 = _sut.Hash(password);
        var hash2 = _sut.Hash(password);

        hash1.Should().NotBe(hash2);
    }

    /// <summary>
    /// Verifies that a correct password matches its hash.
    /// </summary>
    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var password = "MySecureP@ss1";
        var hash = _sut.Hash(password);

        var result = _sut.Verify(password, hash);

        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an incorrect password does not match the hash.
    /// </summary>
    [Fact]
    public void Verify_WithIncorrectPassword_ReturnsFalse()
    {
        var password = "MySecureP@ss1";
        var hash = _sut.Hash(password);

        var result = _sut.Verify("WrongPassword", hash);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that password verification is case-sensitive.
    /// </summary>
    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var password = "MySecureP@ss1";
        var hash = _sut.Hash(password);

        var result = _sut.Verify("mysecurep@ss1", hash);

        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the hash output starts with the expected BCrypt prefix ($2a$, $2b$, or $2y$).
    /// </summary>
    [Fact]
    public void Hash_Output_IsValidBCryptHash()
    {
        var password = "MySecureP@ss1";

        var hash = _sut.Hash(password);

        // BCrypt hashes start with $2a$, $2b$, or $2y$
        hash.Should().MatchRegex("^\\$2[aby]\\$\\d{2}\\$");
    }
}
