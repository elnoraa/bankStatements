using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

/// <summary>
/// Unit tests for <see cref="ExternalLoginRequest"/> validation attributes.
/// </summary>
public sealed class ExternalLoginRequestValidationTests
{
    /// <summary>
    /// Verifies that a valid external login request passes all validation rules.
    /// </summary>
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

    /// <summary>
    /// Verifies that a missing provider fails validation.
    /// </summary>
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

    /// <summary>
    /// Verifies that a missing ID token fails validation.
    /// </summary>
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
