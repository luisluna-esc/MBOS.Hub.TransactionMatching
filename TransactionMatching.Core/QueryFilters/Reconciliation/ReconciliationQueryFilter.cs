namespace TransactionMatching.Core.QueryFilters.Reconciliation;

public class ReconciliationQueryFilter
{
    public long? BankAccountId { get; set; }
    public int? Year { get; set; }
    /// <summary>Open o Closed.</summary>
    public string? Status { get; set; }
}

public class BankAccountQueryFilter
{
    public bool? IsActive { get; set; }
}
