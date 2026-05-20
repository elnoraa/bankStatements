using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

public sealed class ExternalCodeRequestValidationTests
{
    [Fact]
    public void WithValidData_PassesValidation()
    {
        var request = new ExternalCodeRequest
        {
            Provider = "google",
            Code = "auth_code_123",
            CodeVerifier = "verifier_123",
            RedirectUri = "http://localhost:3000/callback"
        };

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void WithMissingProvider_FailsValidation()
    {
        var request = new ExternalCodeRequest
        {
            Provider = null!,
            Code = "auth_code_123",
            CodeVerifier = "verifier_123",
            RedirectUri = "http://localhost:3000/callback"
        };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Provider"));
    }

    [Fact]
    public void WithMissingCode_FailsValidation()
    {
        var request = new ExternalCodeRequest
        {
            Provider = "google",
            Code = null!,
            CodeVerifier = "verifier_123",
            RedirectUri = "http://localhost:3000/callback"
        };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Code"));
    }

    [Fact]
    public void WithMissingCodeVerifier_FailsValidation()
    {
        var request = new ExternalCodeRequest
        {
            Provider = "google",
            Code = "auth_code_123",
            CodeVerifier = null!,
            RedirectUri = "http://localhost:3000/callback"
        };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("CodeVerifier"));
    }

    [Fact]
    public void WithMissingRedirectUri_FailsValidation()
    {
        var request = new ExternalCodeRequest
        {
            Provider = "google",
            Code = "auth_code_123",
            CodeVerifier = "verifier_123",
            RedirectUri = null!
        };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("RedirectUri"));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        return results;
    }
}
