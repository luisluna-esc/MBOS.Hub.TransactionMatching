using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>Un par propuesto: índice en la lista del banco, índice en la del Mayor, puntaje y estado.</summary>
public record MatchProposal(int BankIndex, int LedgerIndex, int Score, string Status);

/// <summary>
/// Coteja el banco con ASSINET como lo hacía contabilidad a mano, pero mirando las tres cosas a la
/// vez: el IMPORTE tiene que ser igual (al centavo); entre los de igual importe gana el de FECHA
/// más cercana (la del banco contra la que va en la descripción del Mayor) y NOMBRE más parecido
/// (el banco trae el nombre completo, ASSINET apellido paterno y nombre, a veces con errores de
/// tipeo: "Wilfredso", "Callizaya"). Así, entre varios depósitos de 333 cada uno va con su persona.
///
/// Ingresos (banco Crédito ↔ Mayor Débito) y egresos (banco Débito ↔ Mayor Crédito) se cotejan por
/// separado. Función pura: no toca la base.
/// </summary>
public static class MatchEngine
{
    /// <summary>Desde este puntaje el par se da por cotejado sin revisar.</summary>
    public const int AutoScore = 80;
    /// <summary>Desde este puntaje se propone, para que contabilidad lo confirme.</summary>
    public const int SuggestScore = 50;

    public static List<MatchProposal> Match(IReadOnlyList<BankMovement> bank, IReadOnlyList<LedgerEntry> ledger)
    {
        var bankNames = bank.Select(NameMatcher.BankTokens).ToList();
        var ledgerNames = ledger.Select(e => NameMatcher.Tokens(e.CounterpartyName)).ToList();

        // Índice del Mayor por (sentido, importe): solo se comparan los que pueden ser pareja.
        var ledgerByKey = ledger
            .Select((entry, index) => (entry, index))
            .GroupBy(x => (Income: x.entry.Debit > 0, Amount: x.entry.Debit > 0 ? x.entry.Debit : x.entry.Credit))
            .ToDictionary(g => g.Key, g => g.Select(x => x.index).ToList());

        var candidates = new List<(int Bank, int Ledger, int Score, int DayGap)>();
        for (var b = 0; b < bank.Count; b++)
        {
            var movement = bank[b];
            var income = movement.Credit > 0;
            var amount = income ? movement.Credit : movement.Debit;
            if (!ledgerByKey.TryGetValue((income, amount), out var sameAmount))
            {
                continue;
            }
            foreach (var l in sameAmount)
            {
                var dayGap = DayGap(movement, ledger[l]);
                var score = Score(dayGap, NameMatcher.Similarity(bankNames[b], ledgerNames[l]));
                if (score >= SuggestScore)
                {
                    candidates.Add((b, l, score, dayGap));
                }
            }
        }

        // Los mejores pares primero; cada fila se usa una sola vez.
        var usedBank = new HashSet<int>();
        var usedLedger = new HashSet<int>();
        var result = new List<MatchProposal>();
        foreach (var c in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.DayGap).ThenBy(c => c.Bank).ThenBy(c => c.Ledger))
        {
            if (usedBank.Contains(c.Bank) || usedLedger.Contains(c.Ledger))
            {
                continue;
            }
            usedBank.Add(c.Bank);
            usedLedger.Add(c.Ledger);
            result.Add(new MatchProposal(c.Bank, c.Ledger, c.Score, c.Score >= AutoScore ? MatchStatus.Auto : MatchStatus.Suggested));
        }
        return result;
    }

    /// <summary>Puntaje (0-100) de dos filas con el mismo importe: mitad fecha, mitad nombre.</summary>
    public static int Score(int dayGap, double nameSimilarity) =>
        (int)Math.Round(50 * DateCloseness(dayGap) + 50 * nameSimilarity);

    /// <summary>Días entre el banco y la fecha de la descripción del Mayor (o la contable si no trae).</summary>
    public static int DayGap(BankMovement movement, LedgerEntry entry)
    {
        var ledgerDate = entry.MovementDate ?? entry.AccountingDate;
        return Math.Abs(movement.Date.DayNumber - ledgerDate.DayNumber);
    }

    private static double DateCloseness(int days) => days switch
    {
        0 => 1.0,
        1 => 0.9,
        2 => 0.8,
        3 => 0.7,
        <= 7 => 0.5,
        <= 15 => 0.2,
        _ => 0.0,
    };
}
