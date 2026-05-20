using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

public sealed class LoginRequestValidationTests
{
    [Fact]
    public void WithValidData_PassesValidation()
    {
        var request = new LoginRequest(
            Email: "test@example.com",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void WithMissingEmail_FailsValidation()
    {
        var request = new LoginRequest(
            Email: null!,
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    [Fact]
    public void WithInvalidEmailFormat_FailsValidation()
    {
        var request = new LoginRequest(
            Email: "not-an-email",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    [Fact]
    public void WithMissingPassword_FailsValidation()
    {
        var request = new LoginRequest(
            Email: "test@example.com",
            Password: null!);

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    [Fact]
    public void WithShortPassword_FailsValidation()
    {
        var request = new LoginRequest(
            Email: "test@example.com",
            Password: "1234567");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        return results;
    }
}
