using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

/// <summary>
/// Unit tests for <see cref="ExternalCodeRequest"/> validation attributes.
/// </summary>
public sealed class ExternalCodeRequestValidationTests
{
    /// <summary>
    /// Verifies that a valid external code request passes all validation rules.
    /// </summary>
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

    /// <summary>
    /// Verifies that a missing provider fails validation.
    /// </summary>
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

    /// <summary>
    /// Verifies that a missing authorization code fails validation.
    /// </summary>
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

    /// <summary>
    /// Verifies that a missing code verifier fails validation.
    /// </summary>
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

    /// <summary>
    /// Verifies that a missing redirect URI fails validation.
    /// </summary>
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

    /// <summary>
    /// Validates the specified instance using <see cref="ValidationContext"/> and <see cref="Validator.TryValidateObject"/>.
    /// </summary>
    /// <param name="instance">The object to validate.</param>
    /// <returns>A list of validation results.</returns>
    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance);
        Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        return results;
    }
}
