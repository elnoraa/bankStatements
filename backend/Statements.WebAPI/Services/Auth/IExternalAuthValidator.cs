using System.Threading;

namespace Statements.WebAPI.Services.Auth;

public sealed record ExternalUserInfo(string Provider, string ProviderKey, string? Email, string? DisplayName, bool EmailVerified);

public interface IExternalAuthValidator
{
    Task<ExternalUserInfo> ValidateAsync(string provider, string idToken, CancellationToken cancellationToken);
}
