using System.Data;

namespace Statements.WebAPI.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
