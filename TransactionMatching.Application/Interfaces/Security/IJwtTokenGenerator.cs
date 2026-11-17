using TransactionMatching.Core.Entities.Security;

namespace TransactionMatching.Application.Interfaces.Security;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(AppUser appUser, IEnumerable<string> roles);
    string GenerateRefreshToken();
}
