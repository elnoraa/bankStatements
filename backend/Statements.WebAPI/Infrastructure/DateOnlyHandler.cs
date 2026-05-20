using System.Data;
using Dapper;

namespace Statements.WebAPI.Infrastructure;

/// <summary>
/// Custom Dapper type handler to map <see cref="DateOnly"/> to/from database date values.
/// Required because Dapper's built-in DateOnly support is not available in version 2.1.72.
/// </summary>
internal sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }
}

/// <summary>
/// Handler for nullable DateOnly values used as Dapper query parameters.
/// </summary>
internal sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override DateOnly? Parse(object value)
    {
        if (value is null || value == DBNull.Value)
            return null;
        return DateOnly.FromDateTime((DateTime)value);
    }

    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
    {
        if (value.HasValue)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.Value.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
    }
}
