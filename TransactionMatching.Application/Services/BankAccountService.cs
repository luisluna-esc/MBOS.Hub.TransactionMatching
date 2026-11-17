using System.Net;
using Microsoft.EntityFrameworkCore;
using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Application.Interfaces.Reconciliation;
using TransactionMatching.Core.Configuration;
using TransactionMatching.Core.Entities.Reconciliation;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.Interfaces;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Reconciliation;

namespace TransactionMatching.Application.Services;

// Las cuentas se crean solas al subir el primer extracto: aquí solo se les cambia el nombre o se
// desactivan. El número de cuenta no se edita porque es con lo que se reconocen los archivos.
public class BankAccountService : IBankAccountService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly PaginationOptions _paginationOptions;

    public BankAccountService(IUnitOfWork unitOfWork, PaginationOptions paginationOptions)
    {
        _unitOfWork = unitOfWork;
        _paginationOptions = paginationOptions;
    }

    public async Task<ResponseGetObject> GetAll(PaginationQueryFilter paginationQueryFilter, BankAccountQueryFilter filter)
    {
        var pageSize = paginationQueryFilter.PageSize > 0 ? paginationQueryFilter.PageSize : _paginationOptions.InitialPageSize;
        var pageNumber = paginationQueryFilter.PageNumber > 0 ? paginationQueryFilter.PageNumber : _paginationOptions.InitialPageNumber;

        var query = _unitOfWork.Repository<BankAccount>().Query();
        if (filter.IsActive.HasValue)
        {
            query = query.Where(a => a.IsActive == filter.IsActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(paginationQueryFilter.SearchCriteria))
        {
            var search = paginationQueryFilter.SearchCriteria;
            query = query.Where(a => a.Name.Contains(search) || a.AccountNumber.Contains(search) || (a.Department != null && a.Department.Contains(search)));
        }

        var totalRecords = await query.CountAsync();
        var items = await query.OrderBy(a => a.Department).ThenBy(a => a.Bank).ThenBy(a => a.Name).Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(a => new BankAccountDto
            {
                BankAccountId = a.BankAccountId,
                Bank = a.Bank,
                AccountNumber = a.AccountNumber,
                Name = a.Name,
                Department = a.Department,
                LedgerSubaccount = a.LedgerSubaccount,
                IsActive = a.IsActive,
            })
            .ToListAsync();

        return Responses.Ok(new PagedResult<BankAccountDto> { Items = items, TotalRecords = totalRecords, PageNumber = pageNumber, PageSize = pageSize });
    }

    public async Task<ResponsePost> Update(long id, BankAccountDto request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 100)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "El nombre es obligatorio (hasta 100 caracteres).");
        }

        var repository = _unitOfWork.Repository<BankAccount>();
        var account = await repository.GetByIdAsync(id);
        if (account is null)
        {
            return Responses.PostError(HttpStatusCode.NotFound, "No se encontró la cuenta.");
        }
        if (await repository.Query().AnyAsync(a => a.Name == name && a.BankAccountId != id))
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "Ya hay otra cuenta con ese nombre.");
        }

        var department = request.Department?.Trim();
        if (department?.Length > 100)
        {
            return Responses.PostError(HttpStatusCode.BadRequest, "El departamento puede tener hasta 100 caracteres.");
        }

        account.Name = name;
        account.Department = string.IsNullOrEmpty(department) ? null : department;
        account.IsActive = request.IsActive;
        await _unitOfWork.SaveChangesAsync();
        return Responses.Saved(id, "Cuenta actualizada.");
    }
}
