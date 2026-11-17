using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Application.Interfaces.Reconciliation;
using TransactionMatching.Core.Constants;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Reconciliation;

namespace TransactionMatching.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BankAccountsController : ControllerBase
{
    private readonly IBankAccountService _service;

    public BankAccountsController(IBankAccountService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQueryFilter paginationQueryFilter, [FromQuery] BankAccountQueryFilter filter)
    {
        var result = await _service.GetAll(paginationQueryFilter, filter);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Update(long id, [FromBody] BankAccountDto request)
    {
        var result = await _service.Update(id, request);
        return StatusCode((int)result.StatusCode, result);
    }
}
