using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Infrastructure.Mapping;

// Mapeos de la conciliación: mismas tablas que Scripts/dataBase.sql (snake_case).

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> entity)
    {
        entity.ToTable("bank_account");
        entity.HasKey(e => e.BankAccountId);
        entity.Property(e => e.BankAccountId).HasColumnName("bank_account_id").ValueGeneratedOnAdd();
        entity.Property(e => e.Bank).HasColumnName("bank").HasMaxLength(100).IsRequired();
        entity.Property(e => e.AccountNumber).HasColumnName("account_number").HasMaxLength(30).IsRequired();
        entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        entity.Property(e => e.Department).HasColumnName("department").HasMaxLength(100);
        entity.Property(e => e.LedgerSubaccount).HasColumnName("ledger_subaccount").HasMaxLength(150);
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
    }
}

public class ReconciliationConfiguration : IEntityTypeConfiguration<Reconciliation>
{
    public void Configure(EntityTypeBuilder<Reconciliation> entity)
    {
        entity.ToTable("reconciliation");
        entity.HasKey(e => e.ReconciliationId);
        entity.Property(e => e.ReconciliationId).HasColumnName("reconciliation_id").ValueGeneratedOnAdd();
        entity.Property(e => e.BankAccountId).HasColumnName("bank_account_id");
        entity.Property(e => e.PeriodStart).HasColumnName("period_start");
        entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
        entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        entity.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(10).IsRequired();
        entity.Property(e => e.BankFileName).HasColumnName("bank_file_name").HasMaxLength(255).IsRequired();
        entity.Property(e => e.LedgerFileName).HasColumnName("ledger_file_name").HasMaxLength(255).IsRequired();
        entity.Property(e => e.BankOpeningBalance).HasColumnName("bank_opening_balance").HasPrecision(14, 2);
        entity.Property(e => e.BankClosingBalance).HasColumnName("bank_closing_balance").HasPrecision(14, 2);
        entity.Property(e => e.LedgerOpeningBalance).HasColumnName("ledger_opening_balance").HasPrecision(14, 2);
        entity.Property(e => e.LedgerClosingBalance).HasColumnName("ledger_closing_balance").HasPrecision(14, 2);
        entity.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(1000);
        entity.Property(e => e.CreatedById).HasColumnName("created_by");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
        entity.Property(e => e.ClosedById).HasColumnName("closed_by");
        entity.Property(e => e.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamp without time zone");

        entity.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId);
    }
}

public class BankMovementConfiguration : IEntityTypeConfiguration<BankMovement>
{
    public void Configure(EntityTypeBuilder<BankMovement> entity)
    {
        entity.ToTable("bank_movement");
        entity.HasKey(e => e.BankMovementId);
        entity.Property(e => e.BankMovementId).HasColumnName("bank_movement_id").ValueGeneratedOnAdd();
        entity.Property(e => e.ReconciliationId).HasColumnName("reconciliation_id");
        entity.Property(e => e.LineNumber).HasColumnName("line_number");
        entity.Property(e => e.Date).HasColumnName("movement_date");
        entity.Property(e => e.Time).HasColumnName("movement_time");
        entity.Property(e => e.BankCode).HasColumnName("bank_code").HasMaxLength(30);
        entity.Property(e => e.ChequeNumber).HasColumnName("cheque_number").HasMaxLength(30);
        entity.Property(e => e.TemplateName).HasColumnName("template_name").HasMaxLength(100);
        entity.Property(e => e.DepositCode).HasColumnName("deposit_code").HasMaxLength(50);
        entity.Property(e => e.DepositorDocument).HasColumnName("depositor_document").HasMaxLength(30);
        entity.Property(e => e.DepositorName).HasColumnName("depositor_name").HasMaxLength(200);
        entity.Property(e => e.TransactionType).HasColumnName("transaction_type").HasMaxLength(100);
        entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        entity.Property(e => e.Office).HasColumnName("office").HasMaxLength(100);
        entity.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
        entity.Property(e => e.DepositType).HasColumnName("deposit_type").HasMaxLength(100);
        entity.Property(e => e.RecipientName).HasColumnName("recipient_name").HasMaxLength(200);
        entity.Property(e => e.Glosa).HasColumnName("glosa").HasMaxLength(500);
        entity.Property(e => e.Originator).HasColumnName("originator").HasMaxLength(200);
        entity.Property(e => e.OriginatorAch).HasColumnName("originator_ach").HasMaxLength(200);
        entity.Property(e => e.City).HasColumnName("city").HasMaxLength(50);
        entity.Property(e => e.Debit).HasColumnName("debit").HasPrecision(14, 2);
        entity.Property(e => e.Credit).HasColumnName("credit").HasPrecision(14, 2);
        entity.Property(e => e.Balance).HasColumnName("balance").HasPrecision(14, 2);
        entity.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> entity)
    {
        entity.ToTable("ledger_entry");
        entity.HasKey(e => e.LedgerEntryId);
        entity.Property(e => e.LedgerEntryId).HasColumnName("ledger_entry_id").ValueGeneratedOnAdd();
        entity.Property(e => e.ReconciliationId).HasColumnName("reconciliation_id");
        entity.Property(e => e.LineNumber).HasColumnName("line_number");
        entity.Property(e => e.AccountingDate).HasColumnName("accounting_date");
        entity.Property(e => e.Batch).HasColumnName("batch").HasMaxLength(50);
        entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        entity.Property(e => e.Reference).HasColumnName("reference").HasMaxLength(100);
        entity.Property(e => e.Debit).HasColumnName("debit").HasPrecision(14, 2);
        entity.Property(e => e.Credit).HasColumnName("credit").HasPrecision(14, 2);
        entity.Property(e => e.MovementType).HasColumnName("movement_type").HasMaxLength(50);
        entity.Property(e => e.MovementDate).HasColumnName("movement_date");
        entity.Property(e => e.CounterpartyName).HasColumnName("counterparty_name").HasMaxLength(300);
        entity.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
    }
}

public class ReconciliationMatchConfiguration : IEntityTypeConfiguration<ReconciliationMatch>
{
    public void Configure(EntityTypeBuilder<ReconciliationMatch> entity)
    {
        entity.ToTable("reconciliation_match");
        entity.HasKey(e => e.ReconciliationMatchId);
        entity.Property(e => e.ReconciliationMatchId).HasColumnName("reconciliation_match_id").ValueGeneratedOnAdd();
        entity.Property(e => e.ReconciliationId).HasColumnName("reconciliation_id");
        entity.Property(e => e.BankMovementId).HasColumnName("bank_movement_id");
        entity.Property(e => e.LedgerEntryId).HasColumnName("ledger_entry_id");
        entity.Property(e => e.Score).HasColumnName("score");
        entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        entity.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
        entity.Property(e => e.CreatedById).HasColumnName("created_by");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
    }
}
