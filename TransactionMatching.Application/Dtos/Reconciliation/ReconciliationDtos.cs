namespace TransactionMatching.Application.Dtos.Reconciliation;

/// <summary>Lado de la conciliación: ingresos (banco Crédito ↔ Mayor Débito) o egresos.</summary>
public static class ReconciliationSide
{
    public const string Income = "Income";
    public const string Expense = "Expense";
}

/// <summary>Estado de una línea: los de MatchStatus, o lo que falta registrar en un lado.</summary>
public static class LineStatus
{
    /// <summary>Está en el banco y no en ASSINET: falta registrarlo en la contabilidad.</summary>
    public const string MissingInLedger = "MissingInLedger";
    /// <summary>Está en ASSINET y no en el banco: registrado pero el dinero no entró (o no salió).</summary>
    public const string MissingInBank = "MissingInBank";
}

public class BankAccountDto
{
    public long BankAccountId { get; set; }
    public string Bank { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? LedgerSubaccount { get; set; }
    public bool IsActive { get; set; }
}

public class ReconciliationListItemDto
{
    public long ReconciliationId { get; set; }
    public long BankAccountId { get; set; }
    public string BankAccountName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ToReviewCount { get; set; }
    public int MissingInLedgerCount { get; set; }
    public int MissingInBankCount { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Totales de un lado (ingresos o egresos).</summary>
public class SideSummaryDto
{
    public int BankCount { get; set; }
    public decimal BankTotal { get; set; }
    public int LedgerCount { get; set; }
    public decimal LedgerTotal { get; set; }
    public int MatchedCount { get; set; }
    public int ToReviewCount { get; set; }
    public int MissingInLedgerCount { get; set; }
    public decimal MissingInLedgerTotal { get; set; }
    public int MissingInBankCount { get; set; }
    public decimal MissingInBankTotal { get; set; }
    /// <summary>Pares con importes distintos (solo los emparejados a mano): banco − ASSINET.</summary>
    public decimal AmountDifferences { get; set; }
}

public class BankMovementDto
{
    public long BankMovementId { get; set; }
    public int LineNumber { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? Time { get; set; }
    public string? BankCode { get; set; }
    /// <summary>El nombre más útil: depositante, originador ACH o destinatario.</summary>
    public string? Name { get; set; }
    public string? Document { get; set; }
    public string? Description { get; set; }
    public string? Glosa { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

public class LedgerEntryDto
{
    public long LedgerEntryId { get; set; }
    public int LineNumber { get; set; }
    public DateOnly AccountingDate { get; set; }
    public DateOnly? MovementDate { get; set; }
    public string? Batch { get; set; }
    /// <summary>Comprobante sacado del lote: "MB - 53 1.10" → "MB 53".</summary>
    public string? Voucher { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>Una fila de la conciliación: un par cotejado, o una fila sola de un lado.</summary>
public class ReconciliationLineDto
{
    public string Side { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? MatchId { get; set; }
    public int? Score { get; set; }
    public string? MatchNote { get; set; }
    public BankMovementDto? Bank { get; set; }
    public LedgerEntryDto? Ledger { get; set; }
    /// <summary>Banco − ASSINET (0 cuando coinciden).</summary>
    public decimal Difference { get; set; }
}

public class ReconciliationDetailDto
{
    public long ReconciliationId { get; set; }
    public BankAccountDto BankAccount { get; set; } = new();
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string BankFileName { get; set; } = string.Empty;
    public string LedgerFileName { get; set; } = string.Empty;
    public decimal? BankOpeningBalance { get; set; }
    public decimal? BankClosingBalance { get; set; }
    public decimal? LedgerOpeningBalance { get; set; }
    public decimal? LedgerClosingBalance { get; set; }
    /// <summary>
    /// Saldo de ASSINET más lo que falta registrar en ASSINET, menos lo registrado que no está en el
    /// banco y las diferencias de importe: si todo está explicado, es igual al saldo del banco.
    /// </summary>
    public decimal? ReconciledBalance { get; set; }
    /// <summary>Saldo del banco − saldo conciliado: lo que queda sin explicar (debería ser 0).</summary>
    public decimal? UnexplainedDifference { get; set; }
    public string? Notes { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public SideSummaryDto Income { get; set; } = new();
    public SideSummaryDto Expense { get; set; } = new();
    public List<ReconciliationLineDto> Lines { get; set; } = [];
}

public class ManualMatchDto
{
    public long BankMovementId { get; set; }
    public long LedgerEntryId { get; set; }
    public string? Note { get; set; }
}

public class NoteDto
{
    public string? Note { get; set; }
}
