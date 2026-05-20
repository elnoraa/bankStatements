using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

public sealed class ExternalLoginRequestValidationTests
{
    [Fact]
    public void WithValidData_PassesValidation()
    {
        var request = new ExternalLoginRequest
        {
            Provider = "google",
            IdToken = "eyJhbGci..."
        };

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void WithMissingProvider_FailsValidation()
    {
        var request = new ExternalLoginRequest
        {
            Provider = null!,
            IdToken = "eyJhbGci..."
        };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Provider"));
    }

    [Fact]
    public void WithMissingIdToken_FailsValidation()
    {
        var request = new ExternalLoginRequest
        {
            Provider = "google",
            IdToken = null!
        };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("IdToken"));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        return results;
    }
}
