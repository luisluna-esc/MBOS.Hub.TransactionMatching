namespace TransactionMatching.Core.Entities.Reconciliation;

/// <summary>
/// Una fila del Mayor de ASSINET. En la cuenta de banco, Debit es lo que entró (ingreso) y Credit
/// lo que salió (egreso). La descripción trae el tipo, la fecha real del movimiento y el nombre:
/// "Transferencia 06/10/2026 Lopez Paula"; esas tres partes se guardan separadas para el cotejo.
/// </summary>
public class LedgerEntry
{
    public long LedgerEntryId { get; set; }
    public long ReconciliationId { get; set; }
    public int LineNumber { get; set; }

    /// <summary>Fecha contable del asiento.</summary>
    public DateOnly AccountingDate { get; set; }
    /// <summary>Lote / comprobante (ej. "MB - 15 1.10", "* MR - 1 28.1").</summary>
    public string? Batch { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Reference { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>Primera palabra de la descripción: Transferencia, Deposito, Débito...</summary>
    public string? MovementType { get; set; }
    /// <summary>Fecha que va en la descripción (la del banco); null si no trae.</summary>
    public DateOnly? MovementDate { get; set; }
    /// <summary>Nombre que va en la descripción (apellido paterno y nombre).</summary>
    public string? CounterpartyName { get; set; }

    public string? Note { get; set; }
}
