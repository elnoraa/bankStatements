using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

/// <summary>
/// Unit tests for <see cref="VerifyEmailRequest"/> validation attributes.
/// </summary>
public sealed class VerifyEmailRequestValidationTests
{
    /// <summary>
    /// Verifies that a valid request passes all validation rules.
    /// </summary>
    [Fact]
    public void WithValidData_PassesValidation()
    {
        var request = new VerifyEmailRequest { Token = "valid-token-value" };

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a missing token fails validation.
    /// </summary>
    [Fact]
    public void WithMissingToken_FailsValidation()
    {
        var request = new VerifyEmailRequest { Token = "" };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Token"));
    }

    /// <summary>
    /// Validates a <see cref="VerifyEmailRequest"/> using DataAnnotations validation.
    /// </summary>
    private static List<ValidationResult> Validate(VerifyEmailRequest request)
    {
        var errors = new List<ValidationResult>();
        var context = new ValidationContext(request);
        Validator.TryValidateObject(request, context, errors, validateAllProperties: true);
        return errors;
    }
}
