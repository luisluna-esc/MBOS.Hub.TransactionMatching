namespace TransactionMatching.Core.Entities.Reconciliation;

/// <summary>
/// Un cotejo: un movimiento del banco con su registro en ASSINET (uno con uno).
/// </summary>
public class ReconciliationMatch
{
    public long ReconciliationMatchId { get; set; }
    public long ReconciliationId { get; set; }
    public long BankMovementId { get; set; }
    public long LedgerEntryId { get; set; }
    /// <summary>Qué tan parecidos son (0-100): fecha y nombre; el importe ya es igual.</summary>
    public int Score { get; set; }
    /// <summary>MatchStatus.</summary>
    public string Status { get; set; } = MatchStatus.Auto;
    public string? Note { get; set; }
    public long? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class MatchStatus
{
    /// <summary>El sistema lo cotejó solo: importe, fecha y nombre coinciden.</summary>
    public const string Auto = "Auto";
    /// <summary>El sistema lo propone, pero algo no coincide del todo: hay que revisarlo.</summary>
    public const string Suggested = "Suggested";
    /// <summary>Era una propuesta y contabilidad la confirmó.</summary>
    public const string Confirmed = "Confirmed";
    /// <summary>Lo emparejó contabilidad a mano.</summary>
    public const string Manual = "Manual";

    /// <summary>Los que "Volver a cotejar" respeta: los decidió una persona.</summary>
    public static readonly string[] Kept = [Confirmed, Manual];
}
