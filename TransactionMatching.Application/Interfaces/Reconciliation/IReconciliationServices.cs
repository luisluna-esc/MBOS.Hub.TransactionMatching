using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Application.Dtos.Reports;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.QueryFilters.Pagination;
using TransactionMatching.Core.QueryFilters.Reconciliation;

namespace TransactionMatching.Application.Interfaces.Reconciliation;

public interface IReconciliationService
{
    Task<ResponseGetObject> GetAll(PaginationQueryFilter paginationQueryFilter, ReconciliationQueryFilter filter);
    Task<ResponseGetObject> Get(long id);
    /// <summary>Lee el extracto y el Mayor, crea la conciliación y hace el cotejo automático.</summary>
    Task<ResponsePost> Create(byte[] bankContent, string bankFileName, byte[] ledgerContent, string ledgerFileName, string? scope);
    Task<ResponsePost> Delete(long id);
    /// <summary>Vuelve a cotejar: borra lo que propuso el sistema y respeta lo que decidió contabilidad.</summary>
    Task<ResponsePost> Rematch(long id);
    Task<ResponsePost> MatchManually(long id, ManualMatchDto request);
    Task<ResponsePost> ConfirmMatch(long id, long matchId);
    Task<ResponsePost> ConfirmAllSuggested(long id);
    Task<ResponsePost> Unmatch(long id, long matchId);
    Task<ResponsePost> SetBankMovementNote(long id, long bankMovementId, NoteDto request);
    Task<ResponsePost> SetLedgerEntryNote(long id, long ledgerEntryId, NoteDto request);
    Task<ResponsePost> SetNotes(long id, NoteDto request);
    Task<ResponsePost> SetClosed(long id, bool closed);
    /// <summary>El Excel de la conciliación; con side y/o status, solo esas líneas ("exportar lo que veo").</summary>
    Task<ReportResult> Export(long id, string? side, string? status);
    /// <summary>Reporte anual por meses: depósitos no identificados y registros sin depósito.</summary>
    Task<ReportResult> AnnualReport(int year, long? bankAccountId);
}

public interface IBankAccountService
{
    Task<ResponseGetObject> GetAll(PaginationQueryFilter paginationQueryFilter, BankAccountQueryFilter filter);
    Task<ResponsePost> Update(long id, BankAccountDto request);
}
