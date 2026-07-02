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

    public string GenerateAccessToken(string email, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(email, secretKey, expireMinutes, "access");
    }

    public string GenerateRefreshToken(string email, string secretKey, int expireMinutes)
    {
        return GenerateTokenInternal(email, secretKey, expireMinutes, "refresh");
    }

    private string GenerateTokenInternal(string email, string secretKey, int expireMinutes, string tokenType)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, "User"),
        };

        if (tokenType == "refresh")
        {
            claims.Add(new Claim("token_type", "refresh"));
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

    public string? GetEmailFromToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value;
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
            var email = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "User";

            return new { email, role };
        }
        catch
        {
            return null;
        }
    }
}
