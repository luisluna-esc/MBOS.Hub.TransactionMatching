using System.Text.RegularExpressions;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>
/// Banco Unión ("Extracto de Movimientos", Extracto_Movimientos.xls): arriba "Cuenta:" (con ceros:
/// 10000024770469 es la 1-24770469) y "Desde: dd/mm/aaaa Hasta: dd/mm/aaaa"; después la fila
/// Fecha Movimiento | AG | Descripción | Nro Documento | Monto | Saldo. El monto viene con signo
/// (negativo = sale de la cuenta) y el nombre va dentro de la descripción:
/// "N/C TRASP. DESDE QUISPE VELASQUEZ RONY ORLANDO".
/// </summary>
internal static partial class UnionStatementReader
{
    public static BankStatement Read(List<object?[]> rows)
    {
        var headerIndex = rows.FindIndex(row => row.Any(cell => ExcelRows.Key(cell?.ToString()) == "fecha movimiento"));
        var header = rows[headerIndex];
        int Col(string key) => Array.FindIndex(header, cell => ExcelRows.Key(cell?.ToString()).StartsWith(key));
        var dateCol = Col("fecha movimiento");
        var officeCol = Col("ag");
        var descriptionCol = Col("descripcion");
        var documentCol = Col("nro documento");
        var amountCol = Col("monto");
        var balanceCol = Col("saldo");

        var accountNumber = LabelValue(rows, headerIndex, "cuenta:")
            ?? throw new MatchingFileException("No se encontró el número de cuenta en el extracto de Banco Unión (\"Cuenta:\").");

        var movements = new List<BankMovement>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var date = ExcelRows.Date(Cell(row, dateCol));
            var amount = SignedAmount(Cell(row, amountCol));
            if (date is null || amount == 0)
            {
                continue;
            }

            var description = ExcelRows.Text(row, descriptionCol);
            var name = description is null ? null : FromName().Match(description) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
            movements.Add(new BankMovement
            {
                LineNumber = movements.Count + 1,
                Date = date.Value,
                BankCode = ExcelRows.Text(row, documentCol),
                Description = description,
                DepositorName = amount > 0 ? name : null,
                RecipientName = amount < 0 ? name : null,
                Office = ExcelRows.Text(row, officeCol),
                BankName = BankStatementReader.Union,
                Credit = amount > 0 ? amount : 0,
                Debit = amount < 0 ? -amount : 0,
                Balance = Cell(row, balanceCol) is null ? null : SignedAmount(Cell(row, balanceCol)),
            });
        }

        if (movements.Count == 0)
        {
            throw new MatchingFileException("El extracto de Banco Unión no tiene movimientos.");
        }

        var period = rows.Take(headerIndex).SelectMany(row => row).Select(cell => BankStatementReader.PeriodIn(cell?.ToString())).FirstOrDefault(p => p is not null)
            ?? (movements.Min(m => m.Date), movements.Max(m => m.Date));
        var (opening, closing) = BankStatementReader.Balances(movements);
        return new BankStatement(BankStatementReader.Union, accountNumber, period.Start, period.End, opening, closing, movements);
    }

    /// <summary>"-83,000.00" → -83000; ExcelRows.Amount ya quita las comas.</summary>
    private static decimal SignedAmount(object? value) => ExcelRows.Amount(value);

    private static object? Cell(object?[] row, int index) => index >= 0 && index < row.Length ? row[index] : null;

    private static string? LabelValue(List<object?[]> rows, int untilRow, string label)
    {
        foreach (var row in rows.Take(untilRow))
        {
            var index = Array.FindIndex(row, cell => ExcelRows.Key(cell?.ToString()) == label);
            if (index >= 0)
            {
                var value = row.Skip(index + 1).Select(cell => cell?.ToString()?.Trim()).FirstOrDefault(text => !string.IsNullOrEmpty(text));
                if (value is not null)
                {
                    return NonDigits().Replace(value, "");
                }
            }
        }
        return null;
    }

    /// <summary>"... DESDE NOMBRE" (traspasos recibidos) o "... A NOMBRE" (traspasos enviados).</summary>
    [GeneratedRegex(@"\b(?:DESDE|HACIA)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FromName();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}
