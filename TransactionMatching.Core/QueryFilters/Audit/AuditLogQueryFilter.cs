namespace TransactionMatching.Core.QueryFilters.Audit;

public class AuditLogQueryFilter
{
    public long? AppUserId { get; set; }
    public string? TableName { get; set; }
    public long? RecordId { get; set; }
    public string? Action { get; set; }
}
