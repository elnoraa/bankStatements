using System.Data;
using Dapper;
using FluentAssertions;
using Moq;
using Statements.WebAPI.Infrastructure;

namespace Statements.WebAPI.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="DateOnlyHandler"/>.
/// </summary>
public sealed class DateOnlyHandlerTests
{
    private readonly DateOnlyHandler _sut = new();

    /// <summary>
    /// Verifies that a <see cref="DateTime"/> is correctly parsed to a <see cref="DateOnly"/>.
    /// </summary>
    [Fact]
    public void Parse_WithDateTime_ReturnsDateOnly()
    {
        var dateTime = new DateTime(2025, 1, 15, 10, 30, 0);

        var result = _sut.Parse(dateTime);

        result.Should().Be(new DateOnly(2025, 1, 15));
    }

    /// <summary>
    /// Verifies that <see cref="DateOnlyHandler.SetValue"/> sets DbType.Date and the correct DateTime value.
    /// </summary>
    [Fact]
    public void SetValue_SetsDbTypeDate_AndCorrectValue()
    {
        var parameter = new Mock<IDbDataParameter>();
        var date = new DateOnly(2025, 6, 15);

        _sut.SetValue(parameter.Object, date);

        parameter.VerifySet(p => p.DbType = DbType.Date);
        parameter.VerifySet(p => p.Value = new DateTime(2025, 6, 15, 0, 0, 0));
    }
}

/// <summary>
/// Unit tests for <see cref="NullableDateOnlyHandler"/>.
/// </summary>
public sealed class NullableDateOnlyHandlerTests
{
    private readonly NullableDateOnlyHandler _sut = new();

    /// <summary>
    /// Verifies that a <see cref="DateTime"/> is correctly parsed to a nullable <see cref="DateOnly"/>.
    /// </summary>
    [Fact]
    public void Parse_WithDateTime_ReturnsDateOnlyWrappedInNullable()
    {
        var dateTime = new DateTime(2025, 1, 15, 10, 30, 0);

        var result = _sut.Parse(dateTime);

        result.Should().Be(new DateOnly(2025, 1, 15));
    }

    /// <summary>
    /// Verifies that parsing DBNull.Value returns null.
    /// </summary>
    [Fact]
    public void Parse_WithDBNull_ReturnsNull()
    {
        var result = _sut.Parse(DBNull.Value);

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that parsing a null object returns null.
    /// </summary>
    [Fact]
    public void Parse_WithNull_ReturnsNull()
    {
        var result = _sut.Parse(null!);

        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="NullableDateOnlyHandler.SetValue"/> sets DBNull.Value for null input.
    /// </summary>
    [Fact]
    public void SetValue_WithNull_SetsDBNull()
    {
        var parameter = new Mock<IDbDataParameter>();

        _sut.SetValue(parameter.Object, null);

        parameter.VerifySet(p => p.Value = DBNull.Value);
    }

    /// <summary>
    /// Verifies that <see cref="NullableDateOnlyHandler.SetValue"/> sets DbType.Date and the correct value for a non-null DateOnly.
    /// </summary>
    [Fact]
    public void SetValue_WithValue_SetsDbTypeDate_AndCorrectValue()
    {
        var parameter = new Mock<IDbDataParameter>();
        var date = new DateOnly(2025, 6, 15);

        _sut.SetValue(parameter.Object, date);

        parameter.VerifySet(p => p.DbType = DbType.Date);
        parameter.VerifySet(p => p.Value = new DateTime(2025, 6, 15, 0, 0, 0));
    }
}
