namespace TransactionMatching.Core.Entities.Reconciliation;

/// <summary>
/// Una fila del extracto del banco, con todas sus columnas (así el Excel de salida sale igual que
/// el extracto). Credit es lo que entra a la cuenta (ingreso) y Debit lo que sale (egreso).
/// </summary>
public class BankMovement
{
    public long BankMovementId { get; set; }
    public long ReconciliationId { get; set; }
    /// <summary>Orden de la fila en el extracto.</summary>
    public int LineNumber { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly? Time { get; set; }
    /// <summary>Cod. Bca.: código único del banco (FT…, TT…).</summary>
    public string? BankCode { get; set; }
    public string? ChequeNumber { get; set; }
    public string? TemplateName { get; set; }
    public string? DepositCode { get; set; }
    public string? DepositorDocument { get; set; }
    public string? DepositorName { get; set; }
    public string? TransactionType { get; set; }
    public string? Description { get; set; }
    public string? Office { get; set; }
    public string? BankName { get; set; }
    public string? DepositType { get; set; }
    public string? RecipientName { get; set; }
    public string? Glosa { get; set; }
    public string? Originator { get; set; }
    public string? OriginatorAch { get; set; }
    public string? City { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal? Balance { get; set; }

    /// <summary>Observación de contabilidad (ej. "registrar en ASSINET").</summary>
    public string? Note { get; set; }
}
