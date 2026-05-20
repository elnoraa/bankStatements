using Statements.WebAPI.Contracts.Auth;

namespace Statements.WebAPI.Services.Auth
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
        Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
        Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken cancellationToken);
        Task<AuthResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);
        Task RevokeTokenAsync(string refreshToken, CancellationToken cancellationToken);
    }
}
