using TransactionMatching.Application.Dtos.Security;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Security;

namespace TransactionMatching.Application.Interfaces.Security;

public interface IRoleService
{
    Task<ResponseGetObject> GetAllRoles(PaginationQueryFilter paginationQueryFilter, RoleQueryFilter roleQueryFilter);
    Task<ResponsePost> InsertRole(RoleDto roleDto);
    Task<ResponsePost> UpdateRole(long id, RoleDto roleDto);
    Task<ResponsePost> DeleteRole(long id);
}
