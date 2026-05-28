using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ike.Api.Helpers;

public static class JwtKeyHelper
{
    /// <summary>Decode and validate base64 JWT signing key (HMAC-SHA256 needs >= 32 bytes).</summary>
    public static byte[] GetValidatedSigningKeyBytes(string jwtKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(jwtKeyBase64))
            throw new InvalidOperationException("Jwt:Key is not configured.");

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(jwtKeyBase64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Jwt:Key must be a valid base64-encoded string.");
        }

        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:Key must decode to at least 32 bytes (256 bits) for HMAC-SHA256.");

        return keyBytes;
    }

    /// <summary>Stable base64 key used only when ASPNETCORE_ENVIRONMENT is Testing.</summary>
    public static readonly string TestingJwtKeyBase64 =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("Ike-Test-JWT-Signing-Key-32-Chars!!!"));

    /// <summary>Same rules as JWT bearer configuration: required outside Testing; Testing defaults to <see cref="TestingJwtKeyBase64"/> when unset.</summary>
    public static byte[] ResolveValidatedSigningKeyBytes(IConfiguration configuration, IHostEnvironment environment)
    {
        var jwtKey = configuration["Jwt:Key"]?.Trim();
        if (environment.IsEnvironment("Testing"))
            jwtKey = string.IsNullOrEmpty(jwtKey) ? TestingJwtKeyBase64 : jwtKey;
        else if (string.IsNullOrEmpty(jwtKey))
            throw new InvalidOperationException(
                "Jwt:Key must be configured (base64, min 32 bytes when decoded). " +
                "For local development use: dotnet user-secrets set Jwt:Key \"<base64>\" --project backend/src/Ike.Api/Ike.Api.csproj, environment variable Jwt__Key, or Jwt:Key in appsettings.Development.json.");

        return GetValidatedSigningKeyBytes(jwtKey);
    }
}
