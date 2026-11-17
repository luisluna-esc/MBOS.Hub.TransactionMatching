using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Application.Interfaces.Reconciliation;
using TransactionMatching.Core.Constants;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Reconciliation;

namespace TransactionMatching.Web.Controllers;

// Todos consultan y descargan; solo M-BOS y Contabilidad suben archivos y cotejan.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReconciliationsController : ControllerBase
{
    private readonly IReconciliationService _service;

    public ReconciliationsController(IReconciliationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] PaginationQueryFilter paginationQueryFilter, [FromQuery] ReconciliationQueryFilter filter) =>
        Result(await _service.GetAll(paginationQueryFilter, filter));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id) => Result(await _service.Get(id));

    /// <summary>Sube el extracto del banco (bankFile) y el Mayor de ASSINET (ledgerFile) y los coteja.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.Writers)]
    [RequestSizeLimit(24 * 1024 * 1024)]
    public async Task<IActionResult> Create(IFormFile? bankFile, IFormFile? ledgerFile) =>
        Result(await _service.Create(await Read(bankFile), bankFile?.FileName ?? "", await Read(ledgerFile), ledgerFile?.FileName ?? ""));

    [HttpDelete("{id}")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Delete(long id) => Result(await _service.Delete(id));

    [HttpPost("{id}/rematch")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Rematch(long id) => Result(await _service.Rematch(id));

    [HttpPost("{id}/matches")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Match(long id, [FromBody] ManualMatchDto request) => Result(await _service.MatchManually(id, request));

    [HttpPost("{id}/matches/{matchId}/confirm")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Confirm(long id, long matchId) => Result(await _service.ConfirmMatch(id, matchId));

    [HttpPost("{id}/matches/confirm-all")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> ConfirmAll(long id) => Result(await _service.ConfirmAllSuggested(id));

    [HttpDelete("{id}/matches/{matchId}")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Unmatch(long id, long matchId) => Result(await _service.Unmatch(id, matchId));

    [HttpPut("{id}/bank-movements/{movementId}/note")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> BankNote(long id, long movementId, [FromBody] NoteDto request) =>
        Result(await _service.SetBankMovementNote(id, movementId, request));

    [HttpPut("{id}/ledger-entries/{entryId}/note")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> LedgerNote(long id, long entryId, [FromBody] NoteDto request) =>
        Result(await _service.SetLedgerEntryNote(id, entryId, request));

    [HttpPut("{id}/notes")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Notes(long id, [FromBody] NoteDto request) => Result(await _service.SetNotes(id, request));

    [HttpPost("{id}/close")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Close(long id) => Result(await _service.SetClosed(id, true));

    [HttpPost("{id}/reopen")]
    [Authorize(Roles = RoleNames.Writers)]
    public async Task<IActionResult> Reopen(long id) => Result(await _service.SetClosed(id, false));

    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(long id)
    {
        var result = await _service.Export(id);
        return result.File is { } file
            ? File(file.Content, file.ContentType, file.FileName)
            : StatusCode((int)result.Error!.StatusCode, result.Error);
    }

    private static async Task<byte[]> Read(IFormFile? file)
    {
        if (file is null)
        {
            return [];
        }
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        return stream.ToArray();
    }

    private ObjectResult Result(ResponseGetObject result) => StatusCode((int)result.StatusCode, result);
    private ObjectResult Result(ResponsePost result) => StatusCode((int)result.StatusCode, result);
}
