namespace TransactionMatching.Core.QueryFilters.Security;

public class MenuQueryFilter
{
    public long? MenuId { get; set; }
    public long? ParentMenuId { get; set; }
    public bool? IsActive { get; set; }
}
