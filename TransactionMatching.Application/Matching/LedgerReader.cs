using System.Text.RegularExpressions;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>Lo que trae el Mayor: la subcuenta del banco, los saldos y los asientos.</summary>
public record Ledger(
    string? Subaccount,
    string? AccountNumber,
    decimal? OpeningBalance,
    decimal ClosingBalance,
    List<LedgerEntry> Entries);

/// <summary>
/// Lee el "Libro Mayor [Sub Cuenta]" de ASSINET (.xlsx) tal como se descarga: encabezado con la
/// cuenta y la subcuenta del banco ("BMSC M/N 1000055642 Suc. 00"), la fila de títulos (Fecha,
/// Lote, Descripción, ..., Débito, Crédito, Saldo D/C), el saldo inicial y los asientos.
/// </summary>
public static partial class LedgerReader
{
    /// <summary>La fila de títulos del Mayor: Lote y Débito en la misma fila.</summary>
    internal static bool LooksLikeLedger(List<object?[]> rows) =>
        rows.Any(row => row.Any(cell => ExcelRows.Key(cell?.ToString()) == "lote") && row.Any(cell => ExcelRows.Key(cell?.ToString()) == "debito"));

    public static Ledger Read(byte[] content)
    {
        if (content.Length > 4 && content[0] == '%' && content[1] == 'P')
        {
            throw new MatchingFileException("Ese archivo es un PDF; el Mayor de ASSINET se descarga en Excel. ¿Se invirtieron los archivos?");
        }
        var rows = ExcelRows.FirstSheet(content);
        if (!LooksLikeLedger(rows) && rows.Any(row => row.Any(cell => ExcelRows.Key(cell?.ToString()) is "credito" or "fecha movimiento")))
        {
            throw new MatchingFileException("Ese archivo es un extracto del banco, no el Mayor de ASSINET. ¿Se invirtieron los archivos?");
        }

        var headerIndex = rows.FindIndex(row =>
            row.Any(cell => ExcelRows.Key(cell?.ToString()) == "lote") && row.Any(cell => ExcelRows.Key(cell?.ToString()) == "debito"));
        if (headerIndex < 0)
        {
            throw new MatchingFileException("El archivo de ASSINET no tiene la fila de títulos (Fecha, Lote, Descripción, Débito, Crédito). ¿Es el Libro Mayor descargado de ASSINET?");
        }

        var header = rows[headerIndex];
        int Col(string key) => Array.FindIndex(header, cell => ExcelRows.Key(cell?.ToString()) == key);
        var dateCol = Col("fecha");
        var batchCol = Col("lote");
        var descriptionCol = Col("descripcion");
        var referenceCol = Col("nro.ref.");
        var debitCol = Col("debito");
        var creditCol = Col("credito");
        var balanceCol = Array.FindIndex(header, cell => ExcelRows.Key(cell?.ToString()).StartsWith("saldo"));

        // El bloque Fondo / Cuenta / Subcuenta va debajo de la fila de títulos, antes del saldo inicial.
        var subaccount = SubaccountLabel(rows, headerIndex + 15);
        decimal? opening = null;
        var entries = new List<LedgerEntry>();

        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var description = Description(row, descriptionCol);
            var key = ExcelRows.Key(description);

            if (key.StartsWith("saldo inicial"))
            {
                opening = ExcelRows.Amount(Cell(row, balanceCol));
                continue;
            }
            if (key.StartsWith("saldo final"))
            {
                break;
            }

            var date = ExcelRows.Date(Cell(row, dateCol));
            var debit = ExcelRows.Amount(Cell(row, debitCol));
            var credit = ExcelRows.Amount(Cell(row, creditCol));
            if (date is null || (debit == 0 && credit == 0))
            {
                continue;
            }

            var entry = new LedgerEntry
            {
                LineNumber = entries.Count + 1,
                AccountingDate = date.Value,
                Batch = ExcelRows.Text(row, batchCol),
                Description = description ?? string.Empty,
                Reference = ExcelRows.Text(row, referenceCol),
                Debit = debit,
                Credit = credit,
            };
            ParseDescription(entry);
            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            throw new MatchingFileException("El Mayor de ASSINET no tiene asientos.");
        }

        var closing = (opening ?? 0) + entries.Sum(e => e.Debit) - entries.Sum(e => e.Credit);
        var accountNumber = subaccount is null ? null : LongestNumber(subaccount);
        return new Ledger(subaccount, accountNumber, opening, closing, entries);
    }

    /// <summary>
    /// "Transferencia 06/10/2026 Lopez Paula" (o "01/10/26") → tipo, fecha y nombre. Si no sigue ese formato, el
    /// nombre es todo el texto y la fecha queda vacía (el cotejo usa entonces la fecha contable).
    /// </summary>
    public static void ParseDescription(LedgerEntry entry)
    {
        var match = DescriptionPattern().Match(entry.Description);
        if (match.Success)
        {
            entry.MovementType = match.Groups["type"].Value;
            entry.MovementDate = ExcelRows.Date(match.Groups["date"].Value);
            entry.CounterpartyName = match.Groups["name"].Value.Trim();
        }
        else
        {
            entry.CounterpartyName = entry.Description.Trim();
        }
    }

    /// <summary>La descripción puede venir partida en varias celdas combinadas: se toma la primera con texto.</summary>
    private static string? Description(object?[] row, int descriptionCol)
    {
        for (var i = descriptionCol; i >= 0 && i < row.Length && i < descriptionCol + 3; i++)
        {
            var text = ExcelRows.Text(row, i);
            if (text is not null)
            {
                return text;
            }
        }
        return null;
    }

    private static object? Cell(object?[] row, int index) => index >= 0 && index < row.Length ? row[index] : null;

    /// <summary>La etiqueta que sigue a "Subcuenta:" (la del banco), en las filas de arriba.</summary>
    private static string? SubaccountLabel(List<object?[]> rows, int untilRow)
    {
        foreach (var row in rows.Take(untilRow))
        {
            var index = Array.FindIndex(row, cell => ExcelRows.Key(cell?.ToString()) == "subcuenta:");
            if (index >= 0)
            {
                var texts = row.Skip(index + 1).Select(cell => cell?.ToString()?.Trim()).Where(text => !string.IsNullOrEmpty(text)).ToList();
                return texts.FirstOrDefault(text => text!.Length > 3) ?? texts.LastOrDefault();
            }
        }
        return null;
    }

    /// <summary>El número de cuenta de la etiqueta, solo dígitos ("1-24770469" → "124770469").</summary>
    private static string? LongestNumber(string text) =>
        Numbers().Matches(text).Select(m => m.Value.Replace("-", "")).OrderByDescending(n => n.Length).FirstOrDefault(n => n.Length >= 6);

    [GeneratedRegex(@"^\s*(?<type>\S+)\s+(?<date>\d{1,2}/\d{1,2}/(?:\d{4}|\d{2}))\s+(?<name>.+?)\s*$")]
    private static partial Regex DescriptionPattern();

    [GeneratedRegex(@"\d[\d-]*\d")]
    private static partial Regex Numbers();
}
