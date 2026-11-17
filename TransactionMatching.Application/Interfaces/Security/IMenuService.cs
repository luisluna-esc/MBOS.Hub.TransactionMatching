using TransactionMatching.Application.Dtos.Security;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Security;

namespace TransactionMatching.Application.Interfaces.Security;

public interface IMenuService
{
    Task<ResponseGetObject> GetAllMenus(PaginationQueryFilter paginationQueryFilter, MenuQueryFilter menuQueryFilter);
    Task<ResponsePost> InsertMenu(MenuDto menuDto);
    Task<ResponsePost> UpdateMenu(long id, MenuDto menuDto);
    Task<ResponsePost> DeleteMenu(long id);
    Task<ResponseGetObject> GetMyMenu();
}
