using System.Text.RegularExpressions;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>Lo que trae el extracto: la cuenta, el período y las filas.</summary>
public record BankStatement(
    string Bank,
    string AccountNumber,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    List<BankMovement> Movements);

/// <summary>
/// Lee el extracto de cualquiera de los tres bancos tal como se descarga, y reconoce cuál es por
/// su contenido (no por el nombre del archivo):
/// - Banco Mercantil Santa Cruz: Excel "Extracto de cuenta" (AccountMovementsDetail.xls).
/// - Banco Unión: Excel "Extracto de Movimientos" (Extracto_Movimientos.xls).
/// - Banco Nacional de Bolivia: PDF "Estado de cuenta".
/// </summary>
public static class BankStatementReader
{
    public const string Bmsc = "Banco Mercantil Santa Cruz";
    public const string Union = "Banco Unión";
    public const string Bnb = "Banco Nacional de Bolivia";

    public static BankStatement Read(byte[] content)
    {
        if (IsPdf(content))
        {
            return BnbStatementReader.Read(content);
        }

        var sheets = ExcelRows.Sheets(content);
        var rows = sheets[0];
        if (LedgerReader.LooksLikeLedger(rows))
        {
            throw new MatchingFileException("Ese archivo es el Mayor de ASSINET, no el extracto del banco. ¿Se invirtieron los archivos?");
        }
        if (rows.Any(row => row.Any(cell => ExcelRows.Key(cell?.ToString()) == "fecha movimiento")))
        {
            return UnionStatementReader.Read(rows);
        }
        if (rows.Any(row => ExcelRows.Key(ExcelRows.Text(row, 0)) == "fecha" && row.Any(cell => ExcelRows.Key(cell?.ToString()) == "credito")))
        {
            return BmscStatementReader.Read(rows, sheets.Skip(1).ToList());
        }

        throw new MatchingFileException(
            "No se reconoce el extracto. Se aceptan el extracto del Banco Mercantil (Excel), el de Banco Unión (Excel) y el del BNB (PDF), tal como se descargan.");
    }

    private static bool IsPdf(byte[] content) =>
        content.Length > 4 && content[0] == '%' && content[1] == 'P' && content[2] == 'D' && content[3] == 'F';

    /// <summary>Saldo inicial (antes del primer movimiento) y final, con los saldos de las filas.</summary>
    internal static (decimal? Opening, decimal? Closing) Balances(List<BankMovement> movements)
    {
        var first = movements[0];
        decimal? opening = first.Balance is null ? null : first.Balance - first.Credit + first.Debit;
        return (opening, movements.LastOrDefault(m => m.Balance is not null)?.Balance);
    }

    /// <summary>Las dos primeras fechas de un texto ("Desde: 01/10/2026 Hasta: 31/10/2026").</summary>
    internal static (DateOnly Start, DateOnly End)? PeriodIn(string? text)
    {
        var dates = text is null ? [] : ExcelRows.DatePattern().Matches(text).Select(m => ExcelRows.Date(m.Value)).Where(d => d is not null).ToList();
        return dates.Count >= 2 ? (dates[0]!.Value, dates[1]!.Value) : null;
    }
}

/// <summary>
/// Banco Mercantil Santa Cruz (AccountMovementsDetail.xls): arriba el titular, la cuenta y el
/// período; después una fila de encabezados (Fecha, Hora, Cod. Bca., ..., Débito, Crédito, Saldo) y
/// los movimientos. Las columnas se buscan por su encabezado.
/// </summary>
internal static partial class BmscStatementReader
{
    public static BankStatement Read(List<object?[]> rows, List<List<object?[]>> otherSheets)
    {
        var headerIndex = rows.FindIndex(row =>
            ExcelRows.Key(ExcelRows.Text(row, 0)) == "fecha" && row.Any(cell => ExcelRows.Key(cell?.ToString()) == "credito"));

        var columns = HeaderColumns(rows[headerIndex]);
        var beneficiaries = PayrollBeneficiaries(otherSheets);
        int Col(string key) => columns.TryGetValue(key, out var index) ? index : -1;

        var accountNumber = ValueAfterLabel(rows, headerIndex, "nro de cuenta")
            ?? throw new MatchingFileException("No se encontró el número de cuenta en el extracto (\"Nro de Cuenta:\").");

        var movements = new List<BankMovement>();
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var date = ExcelRows.Date(row[0]);
            var debit = ExcelRows.Amount(Cell(row, Col("debito")));
            var credit = ExcelRows.Amount(Cell(row, Col("credito")));
            if (date is null || (debit == 0 && credit == 0))
            {
                continue;
            }

            movements.Add(new BankMovement
            {
                LineNumber = movements.Count + 1,
                Date = date.Value,
                Time = ExcelRows.Time(Cell(row, Col("hora"))),
                BankCode = ExcelRows.Text(row, Col("cod. bca.")),
                ChequeNumber = ExcelRows.Text(row, Col("nro.cheque")),
                TemplateName = ExcelRows.Text(row, Col("nro/nom.plantilla")),
                DepositCode = ExcelRows.Text(row, Col("cod.dep.num")),
                DepositorDocument = ExcelRows.Text(row, Col("doc.depositante")),
                DepositorName = ExcelRows.Text(row, Col("nombre/denominacion depositante")),
                TransactionType = ExcelRows.Text(row, Col("tipo transact")),
                Description = ExcelRows.Text(row, Col("descripcion")),
                Office = ExcelRows.Text(row, Col("oficina")),
                BankName = ExcelRows.Text(row, Col("banco")),
                DepositType = ExcelRows.Text(row, Col("tipo dep")),
                RecipientName = ExcelRows.Text(row, Col("nom.destinatario")),
                Glosa = ExcelRows.Text(row, Col("glosa")),
                Originator = ExcelRows.Text(row, Col("originador")),
                OriginatorAch = ExcelRows.Text(row, Col("originador ach")),
                City = ExcelRows.Text(row, Col("ciudad origen")),
                Debit = debit,
                Credit = credit,
                Balance = Col("saldo") >= 0 && Cell(row, Col("saldo")) is not null ? ExcelRows.Amount(Cell(row, Col("saldo"))) : null,
            });
        }

        if (movements.Count == 0)
        {
            throw new MatchingFileException("El extracto no tiene movimientos.");
        }

        // Una planilla sale en el extracto como un solo débito a nombre de ASEA MBO; sus beneficiarios
        // están en la hoja "Reporte de Pagos". Se agregan como destinatario para que el cotejo los vea
        // ("Débito 13/10/2026 Flores Mery" con la planilla PRESUPLANZ que le pagó a Mery Flores Huanca).
        foreach (var movement in movements.Where(m => m.Debit > 0 && m.TemplateName is not null))
        {
            if (beneficiaries.TryGetValue(movement.TemplateName!, out var names))
            {
                var joined = string.Join(", ", names);
                movement.RecipientName = joined.Length <= 200 ? joined : joined[..200];
            }
        }

        var period = rows.Take(headerIndex).Select(row => BankStatementReader.PeriodIn(ExcelRows.Text(row, 0))).FirstOrDefault(p => p is not null)
            ?? (movements.Min(m => m.Date), movements.Max(m => m.Date));
        var (opening, closing) = BankStatementReader.Balances(movements);
        return new BankStatement(BankStatementReader.Bmsc, accountNumber, period.Start, period.End, opening, closing, movements);
    }

    /// <summary>"Reporte de Pagos": nombre de la planilla (Nro Planilla) → sus beneficiarios.</summary>
    private static Dictionary<string, List<string>> PayrollBeneficiaries(List<List<object?[]>> sheets)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var headerIndex = sheet.FindIndex(row => row.Any(cell => ExcelRows.Key(cell?.ToString()) == "beneficiario"));
            if (headerIndex < 0)
            {
                continue;
            }
            var header = sheet[headerIndex];
            var payrollCol = Array.FindIndex(header, cell => ExcelRows.Key(cell?.ToString()).StartsWith("nro planilla"));
            var nameCol = Array.FindIndex(header, cell => ExcelRows.Key(cell?.ToString()) == "beneficiario");
            if (payrollCol < 0)
            {
                continue;
            }
            foreach (var row in sheet.Skip(headerIndex + 1))
            {
                var payroll = ExcelRows.Text(row, payrollCol);
                var name = ExcelRows.Text(row, nameCol);
                if (payroll is null || name is null)
                {
                    continue;
                }
                if (!result.TryGetValue(payroll, out var names))
                {
                    result[payroll] = names = [];
                }
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }
        return result;
    }

    /// <summary>Encabezado normalizado (sin tildes, "Cod, Bca," como "cod. bca.") a su columna.</summary>
    private static Dictionary<string, int> HeaderColumns(object?[] header)
    {
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < header.Length; i++)
        {
            var key = ExcelRows.Key(header[i]?.ToString()).Replace(",", ".").Replace(". ", ".").Replace(" .", ".");
            key = key is "cod.bca." or "cod.bca" ? "cod. bca." : key;
            if (key.Length > 0)
            {
                columns.TryAdd(key, i);
            }
        }
        return columns;
    }

    private static object? Cell(object?[] row, int index) => index >= 0 && index < row.Length ? row[index] : null;

    /// <summary>"Nro de Cuenta:" y el valor en alguna celda de la derecha.</summary>
    private static string? ValueAfterLabel(List<object?[]> rows, int untilRow, string label)
    {
        foreach (var row in rows.Take(untilRow))
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (ExcelRows.Key(row[i]?.ToString()).StartsWith(label))
                {
                    var value = row.Skip(i + 1).Select(cell => cell?.ToString()?.Trim()).FirstOrDefault(text => !string.IsNullOrEmpty(text));
                    if (value is not null)
                    {
                        return Digits().Replace(value, "");
                    }
                }
            }
        }
        return null;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex Digits();
}
