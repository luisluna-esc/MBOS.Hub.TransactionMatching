using System.Security.Claims;
using System.Security.Cryptography;
using TransactionMatching.Application.Interfaces.Security;
using TransactionMatching.Core.Configuration;
using TransactionMatching.Core.Entities.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TransactionMatching.Application.Services;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _jwtOptions;

    public JwtTokenGenerator(JwtOptions jwtOptions)
    {
        _jwtOptions = jwtOptions;
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(AppUser appUser, IEnumerable<string> roles)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, appUser.AppUserId.ToString()),
            new(ClaimTypes.Name, appUser.FullName),
            new("username", appUser.Username)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(_jwtOptions.Key)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = signingCredentials
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expiresAt);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}
