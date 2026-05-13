using Tradion.Api.Models;

namespace Tradion.Api.Services;

public interface IJwtTokenService
{
    string GenerateToken(ApplicationUser user, string? role = null);
}
