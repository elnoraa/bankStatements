namespace Statements.WebAPI.Services.Statements;

public interface IStatementParser
{
    IReadOnlyList<ParsedStatementTransaction> Parse(string filePath);
}
