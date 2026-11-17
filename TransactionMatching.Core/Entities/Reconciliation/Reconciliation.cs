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

public static class ReconciliationStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}
