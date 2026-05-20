using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

public sealed class RegisterRequestValidationTests
{
    [Fact]
    public void WithValidData_PassesValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: "Test User",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void WithMissingEmail_FailsValidation()
    {
        var request = new RegisterRequest(
            Email: null!,
            DisplayName: "Test User",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    [Fact]
    public void WithInvalidEmailFormat_FailsValidation()
    {
        var request = new RegisterRequest(
            Email: "not-an-email",
            DisplayName: "Test User",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    [Fact]
    public void WithShortPassword_FailsValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: "Test User",
            Password: "1234567");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    [Fact]
    public void WithMissingPassword_FailsValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: "Test User",
            Password: null!);

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    [Fact]
    public void WithMaxLengthEmail_PassesValidation()
    {
        var localPart = new string('a', 316);
        var request = new RegisterRequest(
            Email: $"{localPart}@b.c",
            DisplayName: "Test User",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void WithTooLongEmail_FailsValidation()
    {
        var localPart = new string('a', 317);
        var request = new RegisterRequest(
            Email: $"{localPart}@b.c",
            DisplayName: "Test User",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    [Fact]
    public void WithTooLongPassword_FailsValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: "Test User",
            Password: new string('a', 201));

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    [Fact]
    public void WithValidDisplayName_PassesValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: "John O'Brien-Smith",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void WithInvalidDisplayNameCharacters_FailsValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: "Test@User!",
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("DisplayName"));
    }

    [Fact]
    public void WithNullDisplayName_PassesValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            DisplayName: null,
            Password: "SecureP@ss1");

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        return results;
    }
}
