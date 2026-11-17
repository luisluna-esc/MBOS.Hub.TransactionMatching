namespace TransactionMatching.Core.Entities.Reconciliation;

/// <summary>
/// Una conciliación: el extracto del banco y el Mayor de ASSINET de una cuenta en un período, con
/// sus movimientos y el cotejo entre ambos.
/// </summary>
public class Reconciliation
{
    public long ReconciliationId { get; set; }
    public long BankAccountId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    /// <summary>ReconciliationStatus: Open o Closed. Cerrada ya no se puede cotejar.</summary>
    public string Status { get; set; } = ReconciliationStatus.Open;
    /// <summary>
    /// ReconciliationScope: qué parte se concilia. Ingresos y egresos los revisan personas distintas,
    /// así que cada una sube los archivos y coteja solo su parte (o las dos juntas con All).
    /// </summary>
    public string Scope { get; set; } = ReconciliationScope.All;

    public string BankFileName { get; set; } = string.Empty;
    public string LedgerFileName { get; set; } = string.Empty;

    public decimal? BankOpeningBalance { get; set; }
    public decimal? BankClosingBalance { get; set; }
    public decimal? LedgerOpeningBalance { get; set; }
    public decimal? LedgerClosingBalance { get; set; }

    public string? Notes { get; set; }
    public long? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? ClosedById { get; set; }
    public DateTime? ClosedAt { get; set; }

    public BankAccount? BankAccount { get; set; }
}

public static class ReconciliationScope
{
    /// <summary>Ingresos y egresos juntos.</summary>
    public const string All = "All";
    /// <summary>Solo ingresos: Crédito del banco contra Débito de ASSINET.</summary>
    public const string Income = "Income";
    /// <summary>Solo egresos: Débito del banco contra Crédito de ASSINET.</summary>
    public const string Expense = "Expense";

    public static readonly string[] Values = [All, Income, Expense];
}

public static class ReconciliationStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}
