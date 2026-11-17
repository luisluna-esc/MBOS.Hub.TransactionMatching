using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;

namespace TransactionMatching.Application.Interfaces.Security;

public interface IRoleMenuService
{
    Task<ResponseGetObject> GetMenusByRole(long roleId);
    Task<ResponsePost> AssignMenu(long roleId, long menuId);
    Task<ResponsePost> RemoveMenu(long roleId, long menuId);
}
