using TransactionMatching.Application.Dtos.Security;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Security;

namespace TransactionMatching.Application.Interfaces.Security;

public interface IAppUserService
{
    Task<ResponseGetObject> GetAllAppUsers(PaginationQueryFilter paginationQueryFilter, AppUserQueryFilter appUserQueryFilter);
    Task<ResponsePost> InsertAppUser(AppUserDto appUserDto);
    Task<ResponsePost> UpdateAppUser(long id, AppUserDto appUserDto);
    Task<ResponsePost> DeleteAppUser(long id);
}
