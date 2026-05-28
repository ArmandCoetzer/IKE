using Ike.Api.Models;

namespace Ike.Api.Services;

public interface IJwtTokenService
{
    string GenerateToken(ApplicationUser user, string? role = null);
}
