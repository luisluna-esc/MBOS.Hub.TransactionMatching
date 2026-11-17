using System.Text.RegularExpressions;
using TransactionMatching.Core.Entities.Reconciliation;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransactionMatching.Application.Matching;

/// <summary>
/// Banco Nacional de Bolivia: el "Estado de cuenta" en PDF. Cada página trae arriba el número de
/// cuenta ("100-0327936") y el período ("Del: 01/10/2026 Al: 31/10/2026"), y una tabla con las
/// columnas FECHA / HORA | SUCURSAL / AGENCIA | DESCRIPCIÓN | CHEQUE/REFERENCIA | CODIGO TRANSAC. |
/// ITF | DÉBITOS | CRÉDITOS | SALDO, que termina en "Totales ->".
///
/// El PDF no tiene celdas: son palabras con su posición. Cada palabra va a la columna cuyo título
/// tiene más cerca (por el centro horizontal), y las palabras a la misma altura forman una fila.
/// Una fila empieza con una fecha; las líneas siguientes sin fecha continúan la descripción.
/// </summary>
internal static partial class BnbStatementReader
{
    private enum Column { Date, Branch, Description, Reference, Code, Itf, Debit, Credit, Balance }

    /// <summary>Palabra del título de cada columna (sin tildes, en minúsculas).</summary>
    private static readonly (Column Column, string Header)[] Headers =
    [
        (Column.Date, "fecha"), (Column.Branch, "sucursal"), (Column.Description, "descripcion"),
        (Column.Reference, "cheque/"), (Column.Code, "codigo"), (Column.Itf, "itf"),
        (Column.Debit, "debitos"), (Column.Credit, "creditos"), (Column.Balance, "saldo"),
    ];

    public static BankStatement Read(byte[] content)
    {
        using var document = PdfDocument.Open(content);

        string? accountNumber = null;
        (DateOnly Start, DateOnly End)? period = null;
        var movements = new List<BankMovement>();

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();
            var lines = Lines(words);
            var pageText = string.Join(' ', lines.Select(line => string.Join(' ', line.Select(w => w.Text))));

            accountNumber ??= AccountPattern().Match(pageText) is { Success: true } a ? a.Value.Replace("-", "") : null;
            period ??= PeriodPattern().Match(pageText) is { Success: true } p ? BankStatementReader.PeriodIn(p.Value) : null;

            var table = TableHeader(words);
            if (table is null)
            {
                continue;
            }
            var (columns, headerBottom) = table.Value;

            BankMovement? current = null;
            foreach (var line in lines)
            {
                // Solo lo que está debajo de los títulos de la tabla.
                if (line[0].BoundingBox.Top >= headerBottom)
                {
                    continue;
                }
                var lineText = string.Join(' ', line.Select(w => w.Text));
                if (lineText.Contains("Totales", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var cells = line.GroupBy(w => Nearest(columns, w)).ToDictionary(g => g.Key, g => string.Join(' ', g.Select(w => w.Text)));
                var date = cells.TryGetValue(Column.Date, out var dateText) ? ExcelRows.Date(dateText) : null;
                if (date is null)
                {
                    // Continuación de la descripción o la referencia de la fila anterior.
                    if (current is not null)
                    {
                        if (cells.TryGetValue(Column.Description, out var more))
                        {
                            current.Description = $"{current.Description} {more}".Trim();
                        }
                        if (cells.TryGetValue(Column.Reference, out var moreReference))
                        {
                            current.Glosa = $"{current.Glosa} {moreReference}".Trim();
                        }
                    }
                    continue;
                }

                var debit = ExcelRows.Amount(cells.GetValueOrDefault(Column.Debit));
                var credit = ExcelRows.Amount(cells.GetValueOrDefault(Column.Credit));
                if (debit == 0 && credit == 0)
                {
                    current = null;
                    continue;
                }

                var reference = cells.GetValueOrDefault(Column.Reference);
                current = new BankMovement
                {
                    LineNumber = movements.Count + 1,
                    Date = date.Value,
                    Time = TimePattern().Match(dateText!) is { Success: true } t ? TimeOnly.Parse(t.Value) : null,
                    Office = cells.GetValueOrDefault(Column.Branch),
                    Description = cells.GetValueOrDefault(Column.Description),
                    ChequeNumber = reference is not null && OnlyDigits().IsMatch(reference) ? reference : null,
                    Glosa = reference is not null && !OnlyDigits().IsMatch(reference) ? reference : null,
                    BankCode = cells.GetValueOrDefault(Column.Code),
                    BankName = BankStatementReader.Bnb,
                    Debit = debit,
                    Credit = credit,
                    Balance = cells.TryGetValue(Column.Balance, out var balance) ? ExcelRows.Amount(balance) : null,
                };
                movements.Add(current);
            }
        }

        if (accountNumber is null)
        {
            throw new MatchingFileException("No se encontró el número de cuenta en el estado de cuenta del BNB (\"100-0327936\").");
        }
        if (movements.Count == 0)
        {
            throw new MatchingFileException("El estado de cuenta del BNB no tiene movimientos (¿es el PDF descargado del banco, no una foto o escaneo?).");
        }

        var (start, end) = period ?? (movements.Min(m => m.Date), movements.Max(m => m.Date));
        var (opening, closing) = BankStatementReader.Balances(movements);
        return new BankStatement(BankStatementReader.Bnb, accountNumber, start, end, opening, closing, movements);
    }

    /// <summary>
    /// Centro horizontal del título de cada columna y dónde termina la fila de títulos; null si la
    /// página no tiene la tabla. Los títulos están en MAYÚSCULAS a la altura de "FECHA": así no se
    /// confunden con "Tot. Débitos" del recuadro de resumen de arriba.
    /// </summary>
    private static (Dictionary<Column, double> Centers, double Bottom)? TableHeader(List<Word> words)
    {
        var fecha = words.FirstOrDefault(w => w.Text == "FECHA");
        if (fecha is null)
        {
            return null;
        }
        var band = words.Where(w => w.BoundingBox.Bottom >= fecha.BoundingBox.Bottom - 15 && w.BoundingBox.Bottom <= fecha.BoundingBox.Top + 12
                                    && w.Text == w.Text.ToUpperInvariant()).ToList();

        var centers = new Dictionary<Column, double>();
        foreach (var (column, header) in Headers)
        {
            var word = band.FirstOrDefault(w => ExcelRows.Key(w.Text) == header);
            if (word is not null)
            {
                centers[column] = word.BoundingBox.Centroid.X;
            }
        }
        return centers.ContainsKey(Column.Date) && centers.ContainsKey(Column.Debit) && centers.ContainsKey(Column.Credit)
            ? (centers, band.Min(w => w.BoundingBox.Bottom))
            : null;
    }

    private static Column Nearest(Dictionary<Column, double> centers, Word word)
    {
        var x = word.BoundingBox.Centroid.X;
        return centers.MinBy(c => Math.Abs(c.Value - x)).Key;
    }

    /// <summary>Palabras agrupadas por renglón (misma altura), de arriba abajo y de izquierda a derecha.</summary>
    private static List<List<Word>> Lines(List<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Bottom - word.BoundingBox.Bottom) < 2.5);
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }
        return lines.Select(l => l.OrderBy(w => w.BoundingBox.Left).ToList()).ToList();
    }

    [GeneratedRegex(@"\b\d{3}-\d{7}\b")]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"Del:\s*\d{1,2}/\d{1,2}/\d{4}\s*Al:\s*\d{1,2}/\d{1,2}/\d{4}")]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex OnlyDigits();
}
