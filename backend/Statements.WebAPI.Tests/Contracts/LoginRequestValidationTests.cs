using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Tests.Contracts;

/// <summary>
/// Unit tests for <see cref="LoginRequest"/> validation attributes.
/// </summary>
public sealed class LoginRequestValidationTests
{
    /// <summary>
    /// Verifies that a valid login request passes all validation rules.
    /// </summary>
    [Fact]
    public void WithValidData_PassesValidation()
    {
        var request = new LoginRequest { Email = "test@example.com", Password = "SecureP@ss1" };

        var errors = Validate(request);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a missing email fails validation.
    /// </summary>
    [Fact]
    public void WithMissingEmail_FailsValidation()
    {
        var request = new LoginRequest { Email = null!, Password = "SecureP@ss1" };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    /// <summary>
    /// Verifies that an invalid email format fails validation.
    /// </summary>
    [Fact]
    public void WithInvalidEmailFormat_FailsValidation()
    {
        var request = new LoginRequest { Email = "not-an-email", Password = "SecureP@ss1" };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Email"));
    }

    /// <summary>
    /// Verifies that a missing password fails validation.
    /// </summary>
    [Fact]
    public void WithMissingPassword_FailsValidation()
    {
        var request = new LoginRequest { Email = "test@example.com", Password = null! };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
    }

    /// <summary>
    /// Verifies that a password shorter than 8 characters fails validation.
    /// </summary>
    [Fact]
    public void WithShortPassword_FailsValidation()
    {
        var request = new LoginRequest { Email = "test@example.com", Password = "1234567" };

        var errors = Validate(request);

        errors.Should().Contain(e => e.MemberNames.Contains("Password"));
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
