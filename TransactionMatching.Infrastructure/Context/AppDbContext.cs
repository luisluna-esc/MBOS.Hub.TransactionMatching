using TransactionMatching.Core.Entities.Reconciliation;
using TransactionMatching.Core.Entities.Audit;
using TransactionMatching.Core.Entities.Security;
using TransactionMatching.Infrastructure.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TransactionMatching.Infrastructure.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppUserRole> AppUserRoles => Set<AppUserRole>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuRole> MenuRoles => Set<MenuRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<BankMovement> BankMovements => Set<BankMovement>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<ReconciliationMatch> ReconciliationMatches => Set<ReconciliationMatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RoleConfiguration).Assembly);

        // All "timestamp" columns in the schema are without time zone, but the app always works in UTC.
        // This converter strips the Kind on write and restores it to Utc on read, so every DateTime
        // property across every entity behaves consistently without repeating this per property.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(nullableUtcConverter);
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
