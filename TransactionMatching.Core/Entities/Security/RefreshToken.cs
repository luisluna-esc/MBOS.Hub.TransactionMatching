namespace TransactionMatching.Core.Entities.Security;

public class RefreshToken
{
    public long RefreshTokenId { get; set; }
    public long AppUserId { get; set; }
    /// <summary>SHA-256 del token que tiene el cliente: si se filtra la base, las sesiones no se pueden usar.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedIp { get; set; }
}
