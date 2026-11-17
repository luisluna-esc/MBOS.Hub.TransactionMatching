using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>Una línea de la conciliación con las entidades (la pantalla y el Excel salen de aquí).</summary>
public record ReconLine(string Side, string Status, ReconciliationMatch? Match, BankMovement? Bank, LedgerEntry? Ledger)
{
    public decimal BankAmount => Bank is null ? 0 : Bank.Credit + Bank.Debit;
    public decimal LedgerAmount => Ledger is null ? 0 : Ledger.Debit + Ledger.Credit;
    public decimal Difference => BankAmount - LedgerAmount;
    public bool IsMissing => Status is LineStatus.MissingInLedger or LineStatus.MissingInBank;
}

/// <summary>Arma las líneas y los totales de una conciliación a partir de sus filas y cotejos.</summary>
public static class ReconciliationLines
{
    public static List<ReconLine> Build(IReadOnlyList<BankMovement> bank, IReadOnlyList<LedgerEntry> ledger, IReadOnlyList<ReconciliationMatch> matches)
    {
        var bankById = bank.ToDictionary(b => b.BankMovementId);
        var ledgerById = ledger.ToDictionary(l => l.LedgerEntryId);
        var matchedBank = matches.Select(m => m.BankMovementId).ToHashSet();
        var matchedLedger = matches.Select(m => m.LedgerEntryId).ToHashSet();

        var lines = new List<ReconLine>();
        foreach (var match in matches)
        {
            var movement = bankById[match.BankMovementId];
            lines.Add(new ReconLine(SideOf(movement), match.Status, match, movement, ledgerById[match.LedgerEntryId]));
        }
        lines.AddRange(bank.Where(b => !matchedBank.Contains(b.BankMovementId))
            .Select(b => new ReconLine(SideOf(b), LineStatus.MissingInLedger, null, b, null)));
        lines.AddRange(ledger.Where(l => !matchedLedger.Contains(l.LedgerEntryId))
            .Select(l => new ReconLine(l.Debit > 0 ? ReconciliationSide.Income : ReconciliationSide.Expense, LineStatus.MissingInBank, null, null, l)));

        // Igual que el control a mano: de menor a mayor importe, y por fecha entre iguales.
        return lines
            .OrderBy(l => l.Side == ReconciliationSide.Income ? 0 : 1)
            .ThenBy(l => l.Bank is not null ? l.BankAmount : l.LedgerAmount)
            .ThenBy(l => l.Bank?.Date ?? l.Ledger!.MovementDate ?? l.Ledger!.AccountingDate)
            .ToList();
    }

    public static SideSummaryDto Summary(IReadOnlyList<ReconLine> lines, string side)
    {
        var sideLines = lines.Where(l => l.Side == side).ToList();
        var missingLedger = sideLines.Where(l => l.Status == LineStatus.MissingInLedger).ToList();
        var missingBank = sideLines.Where(l => l.Status == LineStatus.MissingInBank).ToList();
        return new SideSummaryDto
        {
            BankCount = sideLines.Count(l => l.Bank is not null),
            BankTotal = sideLines.Sum(l => l.BankAmount),
            LedgerCount = sideLines.Count(l => l.Ledger is not null),
            LedgerTotal = sideLines.Sum(l => l.LedgerAmount),
            MatchedCount = sideLines.Count(l => l.Match is not null && l.Status != Core.Entities.Reconciliation.MatchStatus.Suggested),
            ToReviewCount = sideLines.Count(l => l.Status == Core.Entities.Reconciliation.MatchStatus.Suggested),
            MissingInLedgerCount = missingLedger.Count,
            MissingInLedgerTotal = missingLedger.Sum(l => l.BankAmount),
            MissingInBankCount = missingBank.Count,
            MissingInBankTotal = missingBank.Sum(l => l.LedgerAmount),
            AmountDifferences = sideLines.Where(l => l.Match is not null).Sum(l => l.Difference),
        };
    }

    /// <summary>
    /// Saldo de ASSINET llevado al del banco: + ingresos que faltan en ASSINET − egresos que faltan en
    /// ASSINET − ingresos registrados que no están en el banco + egresos registrados que no están en
    /// el banco ± diferencias de importe de los pares.
    /// </summary>
    public static decimal? ReconciledBalance(decimal? ledgerClosing, SideSummaryDto income, SideSummaryDto expense) =>
        ledgerClosing is null
            ? null
            : ledgerClosing
              + income.MissingInLedgerTotal - expense.MissingInLedgerTotal
              - income.MissingInBankTotal + expense.MissingInBankTotal
              + income.AmountDifferences - expense.AmountDifferences;

    private static string SideOf(BankMovement movement) => movement.Credit > 0 ? ReconciliationSide.Income : ReconciliationSide.Expense;
}
