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
