using TransactionMatching.Application.Dtos.Security;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;

namespace TransactionMatching.Application.Interfaces.Security;

public interface IAuthService
{
    Task<ResponseGetObject> Login(LoginDto loginDto, string? ipAddress);
    Task<ResponseGetObject> RefreshToken(RefreshRequestDto refreshRequestDto, string? ipAddress);
    Task<ResponsePost> Logout(LogoutRequestDto logoutRequestDto);
}
