using TransactionMatching.Application.Interfaces.Audit;
using TransactionMatching.Core.Constants;
using TransactionMatching.Core.QueryFilters.Audit;
using TransactionMatching.Core.QueryFilters.Pagination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TransactionMatching.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.AuditViewers)]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLogService;

    public AuditLogsController(IAuditLogService auditLogService)
    {
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQueryFilter paginationQueryFilter, [FromQuery] AuditLogQueryFilter auditLogQueryFilter)
    {
        var result = await _auditLogService.GetAllAuditLogs(paginationQueryFilter, auditLogQueryFilter);
        return StatusCode((int)result.StatusCode, result);
    }
}
