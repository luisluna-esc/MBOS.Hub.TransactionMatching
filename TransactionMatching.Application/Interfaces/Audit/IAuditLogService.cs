using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;
using TransactionMatching.Core.QueryFilters.Audit;
using TransactionMatching.Core.QueryFilters.Pagination;

namespace TransactionMatching.Application.Interfaces.Audit;

public interface IAuditLogService
{
    Task<ResponseGetObject> GetAllAuditLogs(PaginationQueryFilter paginationQueryFilter, AuditLogQueryFilter auditLogQueryFilter);

    /// <summary>Borra la auditoría más antigua que el plazo configurado y devuelve cuántas filas eliminó.</summary>
    Task<int> PurgeExpiredLogs();

    /// <summary>
    /// Registra que el usuario actual consultó (VIEW) o exportó (EXPORT) un reporte. Los cambios
    /// de datos los registra solo el interceptor; las lecturas de reportes se registran aquí
    /// porque la Misión pidió saber también quién consulta y exporta.
    /// </summary>
    Task LogReportAccess(string reportName, string action, string description);
}
