using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TatoToys.Domain.Interfaces;

namespace TatoToys.Infrastructure.Security;

public class JwtTokenService : ITokenService
{
    public const string Issuer = "tatotoys-api";
    public const string Audience = "tatotoys-frontend";

    public string GenerateAccessToken(string userId, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(userId, secretKey, expireMinutes, "access");
    }

    public string GenerateRefreshToken(string userId, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(userId, secretKey, expireMinutes, "refresh");
    }

    public string GenerateRefreshToken(string userId, string firstName, string lastName, string secretKey, int expireMinutes, string? timezone = null)
    {
        return GenerateTokenInternal(userId, secretKey, expireMinutes, "refresh", firstName, lastName, timezone);
    }

    private string GenerateTokenInternal(string userId, string secretKey, int expireMinutes, string tokenType, string? firstName = null, string? lastName = null, string? timezone = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, "User"),
        };

        if (tokenType == "refresh")
        {
            claims.Add(new Claim("token_type", "refresh"));
        }

        if (!string.IsNullOrEmpty(firstName))
        {
            claims.Add(new Claim(ClaimTypes.GivenName, firstName));
        }

        if (!string.IsNullOrEmpty(lastName))
        {
            claims.Add(new Claim(ClaimTypes.Surname, lastName));
        }

        if (tokenType == "refresh" && !string.IsNullOrEmpty(timezone))
        {
            claims.Add(new Claim("timezone", timezone));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string? GetUserIdFromToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        }
        catch
        {
            return null;
        }
    }

    public object? ValidateAndGetUserInfo(string token, string secretKey)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out var validatedToken);

            var jwt = (JwtSecurityToken)validatedToken;
            var userId = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "User";
            var firstName = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value ?? string.Empty;
            var lastName = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value ?? string.Empty;

            return new { userId, role, firstName, lastName };
        }
        catch
        {
            return null;
        }
    }
}
