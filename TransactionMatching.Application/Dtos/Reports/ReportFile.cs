using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;

namespace TransactionMatching.Application.Dtos.Reports;

public enum ReportFormat
{
    Pdf,
    Excel
}

public record ReportFile(byte[] Content, string ContentType, string FileName);

/// <summary>El archivo, o el error para devolver como JSON si no se pudo armar.</summary>
public record ReportResult(ReportFile? File, ResponseGetObject? Error);
