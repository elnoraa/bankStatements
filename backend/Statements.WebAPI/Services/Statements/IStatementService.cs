using Statements.WebAPI.Contracts.Statements;

namespace Statements.WebAPI.Services.Statements;

public interface IStatementService
{
    Task<StatementUploadResponse> UploadAsync(
        Guid userId,
        Guid? bankAccountId,
        IFormFile file,
        CancellationToken cancellationToken);
}
