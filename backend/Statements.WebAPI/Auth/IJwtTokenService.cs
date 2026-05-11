using Statements.WebAPI.Services.Auth;

namespace Statements.WebAPI.Auth;

public interface IJwtTokenService
{
    JwtAccessToken CreateAccessToken(AuthUser user);
}
