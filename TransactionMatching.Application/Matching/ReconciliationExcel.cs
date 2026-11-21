using System.Globalization;
using ClosedXML.Excel;
using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Application.Services;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>Filtro de líneas por estado, igual que los botones de la pantalla de revisión.</summary>
public static class LineFilter
{
    public static bool Matches(string lineStatus, string? filter) => filter switch
    {
        null or "" or "all" => true,
        "matched" => lineStatus is MatchStatus.Auto or MatchStatus.Confirmed or MatchStatus.Manual,
        _ => lineStatus == filter,
    };

    public static string Title(string? side, string? status, string? voucher = null) =>
        string.Join(" · ", new[] { SideText(side), StatusText(status), VoucherText(voucher) }.Where(t => t is not null));

    public static string FileSuffix(string? side, string? status, string? voucher = null) => Title(side, status, voucher).Replace(" · ", " - ");

    private static string? VoucherText(string? voucher) =>
        string.IsNullOrWhiteSpace(voucher) ? null : $"Comprobante {voucher.Trim().ToUpperInvariant()}";

    private static string? SideText(string? side) => side switch
    {
        ReconciliationSide.Income => "Ingresos",
        ReconciliationSide.Expense => "Egresos",
        _ => null,
    };

    private static string? StatusText(string? status) => status switch
    {
        null or "" or "all" => null,
        "matched" => "Cotejados",
        _ => ReconciliationExcel.StatusText(status),
    };
}

/// <summary>
/// El Excel de la conciliación, igual que el control que se hacía a mano ("1. BMSC 5642.xlsx"):
/// hojas INGRESO y EGRESO con las columnas del extracto a la izquierda, el importe del banco, la
/// diferencia, el importe de ASSINET con su fecha, comprobante y detalle a la derecha, y en amarillo
/// lo que falta registrar en un lado. Cada hoja lleva arriba de qué colegio, cuenta y mes es.
/// </summary>
public static class ReconciliationExcel
{
    private static readonly string[] BankHeaders =
    [
        "Fecha", "Hora", "Cod. Bca.", "Nro.Cheque", "Nro/Nom.Plantilla", "Cod.Dep.Num", "Doc.Depositante",
        "Nombre/Denominación Depositante", "Tipo transact", "Descripción", "Oficina", "Banco", "Tipo dep",
        "Nom.Destinatario", "Glosa", "Originador", "Originador ACH", "Ciudad Origen",
    ];

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-BO");
    private static readonly XLColor Missing = XLColor.FromHtml("#FFFF00");
    private static readonly XLColor Review = XLColor.FromHtml("#FCE4D6");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1D3456");
    private const string Money = "#,##0.00";

    /// <summary>Filas de encabezado (título, cuenta, generado) antes de la tabla.</summary>
    private const int TableRow = 5;

    /// <param name="filterTitle">Si viene, es "exportar lo que veo": una sola hoja con esas líneas.</param>
    public static byte[] Build(Reconciliation reconciliation, IReadOnlyList<ReconLine> lines, string? generatedBy, string? filterTitle = null)
    {
        using var workbook = new XLWorkbook();
        var generated = $"Generado el {BoliviaTime.Now:dd/MM/yyyy HH:mm}{(generatedBy is null ? "" : $" por {generatedBy}")}";

        if (filterTitle is not null)
        {
            var side = lines.Select(l => l.Side).Distinct().Count() == 1 ? lines[0].Side : null;
            SideSheet(workbook.AddWorksheet("LISTA"), reconciliation, lines.ToList(), filterTitle, generated, side);
        }
        else
        {
            var income = ReconciliationLines.Summary(lines, ReconciliationSide.Income);
            var expense = ReconciliationLines.Summary(lines, ReconciliationSide.Expense);
            SummarySheet(workbook.AddWorksheet("RESUMEN"), reconciliation, income, expense, generated);
            if (reconciliation.Scope != ReconciliationScope.Expense)
            {
                SideSheet(workbook.AddWorksheet("INGRESO"), reconciliation, lines.Where(l => l.Side == ReconciliationSide.Income).ToList(),
                    "INGRESOS · banco Crédito / ASSINET Débito", generated, ReconciliationSide.Income);
            }
            if (reconciliation.Scope != ReconciliationScope.Income)
            {
                SideSheet(workbook.AddWorksheet("EGRESO"), reconciliation, lines.Where(l => l.Side == ReconciliationSide.Expense).ToList(),
                    "EGRESOS · banco Débito / ASSINET Crédito", generated, ReconciliationSide.Expense);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>"Reid Shepard · BMSC 5412 (cuenta 1001285412) · Octubre 2026".</summary>
    private static string Heading(Reconciliation r)
    {
        var account = r.BankAccount!;
        var who = account.Department is null ? account.Name : $"{account.Department} · {account.Name}";
        return $"Conciliación bancaria · {who} (cuenta {account.AccountNumber}) · {MonthYear(r.PeriodStart)}";
    }

    private static void WriteHeading(IXLWorksheet sheet, string title, string subtitle, string generated, int width)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = subtitle;
        sheet.Cell(2, 1).Style.Font.Bold = true;
        sheet.Cell(3, 1).Value = generated;
        sheet.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
        sheet.Range(1, 1, 1, Math.Max(width, 1)).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
    }

    private static void SideSheet(IXLWorksheet sheet, Reconciliation reconciliation, List<ReconLine> lines, string subtitle, string generated, string? side)
    {
        var (bankHeader, ledgerHeader) = side switch
        {
            ReconciliationSide.Income => ("CRÉDITO - FALTA DE REGISTRO ASSINET", "DÉBITO - FALTA DE REGISTRO BANCO"),
            ReconciliationSide.Expense => ("DÉBITO - FALTA DE REGISTRO ASSINET", "CRÉDITO - FALTA DE REGISTRO BANCO"),
            _ => ("IMPORTE BANCO", "IMPORTE ASSINET"),
        };

        // A-R: extracto | S: banco | T: diferencia | U: ASSINET | V: fecha | W: comprobante | X: detalle | Y: estado | Z: observación
        var headers = BankHeaders.Concat([bankHeader, "DIFERENCIA", ledgerHeader, "Fecha", "COMPROBANTE", "DETALLE", "ESTADO", "OBSERVACIÓN"]).ToArray();
        WriteHeading(sheet, Heading(reconciliation), subtitle, generated, headers.Length);

        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(TableRow, c + 1).Value = headers[c];
        }
        var header = sheet.Range(TableRow, 1, TableRow, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = HeaderFill;
        header.Style.Alignment.WrapText = true;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var row = TableRow + 1;
        foreach (var line in lines)
        {
            if (line.Bank is { } b)
            {
                object?[] values =
                [
                    b.Date.ToString("dd/MM/yyyy"), b.Time?.ToString("HH:mm:ss"), b.BankCode, b.ChequeNumber, b.TemplateName,
                    b.DepositCode, b.DepositorDocument, b.DepositorName, b.TransactionType, b.Description, b.Office, b.BankName,
                    b.DepositType, b.RecipientName, b.Glosa, b.Originator, b.OriginatorAch, b.City,
                ];
                for (var c = 0; c < values.Length; c++)
                {
                    sheet.Cell(row, c + 1).Value = values[c]?.ToString() ?? string.Empty;
                }
                sheet.Cell(row, 19).Value = line.BankAmount;
            }
            else
            {
                sheet.Cell(row, 8).Value = "NO ESTÁ EN EL BANCO";
                sheet.Cell(row, 8).Style.Font.Bold = true;
            }

            sheet.Cell(row, 20).FormulaA1 = $"S{row}-U{row}";

            if (line.Ledger is { } l)
            {
                sheet.Cell(row, 21).Value = line.LedgerAmount;
                sheet.Cell(row, 22).Value = l.AccountingDate.ToString("d/MM/yyyy");
                sheet.Cell(row, 23).Value = l.Batch ?? string.Empty;
                sheet.Cell(row, 24).Value = l.Description;
            }
            else
            {
                sheet.Cell(row, 24).Value = "NO ESTÁ EN ASSINET";
                sheet.Cell(row, 24).Style.Font.Bold = true;
            }

            sheet.Cell(row, 25).Value = StatusText(line.Status);
            sheet.Cell(row, 26).Value = string.Join(" · ", new[] { line.Match?.Note, line.Bank?.Note, line.Ledger?.Note }.Where(n => !string.IsNullOrWhiteSpace(n)));

            if (line.IsMissing)
            {
                sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = Missing;
                sheet.Cell(row, 25).Style.Font.Bold = true;
            }
            else if (line.Status == MatchStatus.Suggested || line.Difference != 0)
            {
                sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = Review;
            }
            row++;
        }

        // Totales.
        sheet.Cell(row, 18).Value = "TOTAL";
        sheet.Cell(row, 19).FormulaA1 = row > TableRow + 1 ? $"SUM(S{TableRow + 1}:S{row - 1})" : "0";
        sheet.Cell(row, 20).FormulaA1 = $"S{row}-U{row}";
        sheet.Cell(row, 21).FormulaA1 = row > TableRow + 1 ? $"SUM(U{TableRow + 1}:U{row - 1})" : "0";
        sheet.Range(row, 18, row, 21).Style.Font.Bold = true;
        sheet.Range(row, 18, row, 21).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        sheet.Range(TableRow + 1, 19, row, 21).Style.NumberFormat.Format = Money;
        sheet.Columns(1, headers.Length).AdjustToContents(TableRow, Math.Min(row, TableRow + 200), 8, 45);
        sheet.Column(1).Width = 12;
        sheet.Column(10).Width = 50;
        sheet.Column(24).Width = 45;
        sheet.Row(TableRow).Height = 32;
        sheet.SheetView.FreezeRows(TableRow);
        sheet.Range(TableRow, 1, Math.Max(row - 1, TableRow), headers.Length).SetAutoFilter();
        sheet.PageSetup.PrintAreas.Add(1, 1, row, headers.Length);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(TableRow, TableRow);
    }

    private static void SummarySheet(IXLWorksheet sheet, Reconciliation r, SideSummaryDto income, SideSummaryDto expense, string generated)
    {
        var account = r.BankAccount!;
        WriteHeading(sheet, Heading(r), $"{account.Bank} · del {r.PeriodStart:dd/MM/yyyy} al {r.PeriodEnd:dd/MM/yyyy} · {ScopeTitle(r.Scope)}", generated, 3);

        var row = TableRow;
        void Line(string label, decimal? value, bool bold = false)
        {
            sheet.Cell(row, 1).Value = label;
            if (value is not null)
            {
                sheet.Cell(row, 2).Value = value.Value;
            }
            sheet.Cell(row, 2).Style.NumberFormat.Format = Money;
            if (bold)
            {
                sheet.Range(row, 1, row, 2).Style.Font.Bold = true;
            }
            row++;
        }

        if (r.Scope == ReconciliationScope.All)
        {
            var reconciled = ReconciliationLines.ReconciledBalance(r.LedgerClosingBalance, income, expense);
            Line("Saldo según ASSINET (Mayor)", r.LedgerClosingBalance, true);
            Line("(+) Ingresos en el banco que faltan registrar en ASSINET", income.MissingInLedgerTotal);
            Line("(−) Egresos en el banco que faltan registrar en ASSINET", expense.MissingInLedgerTotal);
            Line("(−) Ingresos registrados en ASSINET que no están en el banco", income.MissingInBankTotal);
            Line("(+) Egresos registrados en ASSINET que no están en el banco", expense.MissingInBankTotal);
            Line("(±) Diferencias de importe en ingresos cotejados", income.AmountDifferences);
            Line("(±) Diferencias de importe en egresos cotejados", expense.AmountDifferences);
            Line("Saldo conciliado", reconciled, true);
            Line("Saldo según el banco (extracto)", r.BankClosingBalance, true);
            Line("Diferencia sin explicar", reconciled is null || r.BankClosingBalance is null ? null : r.BankClosingBalance - reconciled, true);
        }
        else
        {
            var side = r.Scope == ReconciliationScope.Income ? income : expense;
            var name = r.Scope == ReconciliationScope.Income ? "ingresos" : "egresos";
            Line($"Total {name} según el banco", side.BankTotal, true);
            Line($"Total {name} según ASSINET", side.LedgerTotal, true);
            Line("Faltan registrar en ASSINET", side.MissingInLedgerTotal);
            Line("Registrados en ASSINET que no están en el banco", side.MissingInBankTotal);
            Line("Diferencias de importe en los cotejados", side.AmountDifferences);
        }

        row++;
        sheet.Cell(row, 2).Value = "Ingresos";
        sheet.Cell(row, 3).Value = "Egresos";
        sheet.Range(row, 1, row, 3).Style.Font.Bold = true;
        row++;
        void Count(string label, int a, int b)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = a;
            sheet.Cell(row, 3).Value = b;
            row++;
        }
        Count("Movimientos del banco", income.BankCount, expense.BankCount);
        Count("Registros en ASSINET", income.LedgerCount, expense.LedgerCount);
        Count("Cotejados", income.MatchedCount, expense.MatchedCount);
        Count("Por revisar", income.ToReviewCount, expense.ToReviewCount);
        Count("Falta registro en ASSINET", income.MissingInLedgerCount, expense.MissingInLedgerCount);
        Count("Falta registro en el banco", income.MissingInBankCount, expense.MissingInBankCount);

        if (!string.IsNullOrWhiteSpace(r.Notes))
        {
            row++;
            sheet.Cell(row, 1).Value = "Observaciones";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row + 1, 1).Value = r.Notes;
        }

        sheet.Column(1).Width = 62;
        sheet.Columns(2, 3).Width = 16;
    }

    // ------------------------------------------------------------------------------------------
    // Reporte anual
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reporte anual por meses de todas las cuentas (o una): cuánto quedó sin identificar cada mes.
    /// "No identificados" = depósitos en el banco sin registro en ASSINET; "Sin depósito" = ingresos
    /// registrados en ASSINET que no llegaron al banco (ej. un pago por QR que no entró).
    /// </summary>
    public static byte[] AnnualReport(
        int year,
        IReadOnlyList<BankAccount> accounts,
        IReadOnlyList<Reconciliation> reconciliations,
        IReadOnlyList<BankMovement> unidentified,
        IReadOnlyList<LedgerEntry> notDeposited,
        string? generatedBy)
    {
        using var workbook = new XLWorkbook();
        var generated = $"Generado el {BoliviaTime.Now:dd/MM/yyyy HH:mm}{(generatedBy is null ? "" : $" por {generatedBy}")}";
        var byId = reconciliations.ToDictionary(r => r.ReconciliationId);

        var summary = workbook.AddWorksheet("RESUMEN");
        WriteHeading(summary, $"Reporte anual de conciliaciones {year}", "Importes que quedaron sin cotejar, por mes (— = mes sin conciliar)", generated, 15);
        var row = TableRow;
        row = MonthTable(summary, row, "DEPÓSITOS NO IDENTIFICADOS (en el banco, sin registro en ASSINET)", accounts, reconciliations,
            unidentified.Select(m => (byId[m.ReconciliationId], m.Credit)));
        row = MonthTable(summary, row + 2, "REGISTRADOS SIN DEPÓSITO (en ASSINET, no llegaron al banco)", accounts, reconciliations,
            notDeposited.Select(e => (byId[e.ReconciliationId], e.Debit)));
        summary.Column(1).Width = 40;
        summary.Columns(2, 14).Width = 13;
        summary.SheetView.FreezeColumns(1);

        var detail = workbook.AddWorksheet("NO IDENTIFICADOS");
        WriteHeading(detail, $"Depósitos no identificados {year}", "En el banco y sin registro en ASSINET", generated, 9);
        DetailTable(detail, ["Mes", "Departamento", "Cuenta", "Fecha", "Nombre", "Descripción", "Código", "Importe", "Observación"],
            unidentified.OrderBy(m => byId[m.ReconciliationId].PeriodStart).ThenBy(m => byId[m.ReconciliationId].BankAccount!.Department).ThenBy(m => m.Date)
                .Select(m =>
                {
                    var r = byId[m.ReconciliationId];
                    return new object?[]
                    {
                        MonthYear(r.PeriodStart), r.BankAccount!.Department, r.BankAccount.Name, m.Date.ToString("dd/MM/yyyy"),
                        m.DepositorName ?? m.OriginatorAch ?? m.RecipientName, m.Description, m.BankCode, m.Credit, m.Note,
                    };
                }).ToList(), amountColumn: 8);

        var pending = workbook.AddWorksheet("SIN DEPÓSITO");
        WriteHeading(pending, $"Registrados sin depósito {year}", "Ingresos en ASSINET que no aparecen en el banco", generated, 8);
        DetailTable(pending, ["Mes", "Departamento", "Cuenta", "Fecha", "Comprobante", "Descripción", "Importe", "Observación"],
            notDeposited.OrderBy(e => byId[e.ReconciliationId].PeriodStart).ThenBy(e => byId[e.ReconciliationId].BankAccount!.Department).ThenBy(e => e.MovementDate ?? e.AccountingDate)
                .Select(e =>
                {
                    var r = byId[e.ReconciliationId];
                    return new object?[]
                    {
                        MonthYear(r.PeriodStart), r.BankAccount!.Department, r.BankAccount.Name, (e.MovementDate ?? e.AccountingDate).ToString("dd/MM/yyyy"),
                        e.Batch, e.Description, e.Debit, e.Note,
                    };
                }).ToList(), amountColumn: 7);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Una fila por cuenta, una columna por mes; "—" si ese mes no se concilió.</summary>
    private static int MonthTable(IXLWorksheet sheet, int row, string title, IReadOnlyList<BankAccount> accounts,
        IReadOnlyList<Reconciliation> reconciliations, IEnumerable<(Reconciliation Reconciliation, decimal Amount)> amounts)
    {
        sheet.Cell(row, 1).Value = title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        sheet.Cell(row, 1).Value = "Cuenta";
        for (var month = 1; month <= 12; month++)
        {
            sheet.Cell(row, month + 1).Value = Spanish.TextInfo.ToTitleCase(Spanish.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.'));
        }
        sheet.Cell(row, 14).Value = "Total";
        var header = sheet.Range(row, 1, row, 14);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = HeaderFill;
        var first = ++row;

        var totals = amounts
            .GroupBy(a => (a.Reconciliation.BankAccountId, a.Reconciliation.PeriodStart.Month))
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var reconciled = reconciliations.Select(r => (r.BankAccountId, r.PeriodStart.Month)).ToHashSet();

        foreach (var account in accounts)
        {
            sheet.Cell(row, 1).Value = account.Department is null ? account.Name : $"{account.Department} · {account.Name}";
            for (var month = 1; month <= 12; month++)
            {
                var cell = sheet.Cell(row, month + 1);
                if (!reconciled.Contains((account.BankAccountId, month)))
                {
                    cell.Value = "—";
                    cell.Style.Font.FontColor = XLColor.Gray;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    var amount = totals.GetValueOrDefault((account.BankAccountId, month));
                    cell.Value = amount;
                    if (amount != 0)
                    {
                        cell.Style.Fill.BackgroundColor = Missing;
                    }
                }
            }
            sheet.Cell(row, 14).FormulaA1 = $"SUM(B{row}:M{row})";
            sheet.Cell(row, 14).Style.Font.Bold = true;
            row++;
        }

        sheet.Cell(row, 1).Value = "TOTAL";
        for (var col = 2; col <= 14; col++)
        {
            var letter = XLHelper.GetColumnLetterFromNumber(col);
            sheet.Cell(row, col).FormulaA1 = $"SUM({letter}{first}:{letter}{row - 1})";
        }
        sheet.Range(row, 1, row, 14).Style.Font.Bold = true;
        sheet.Range(row, 1, row, 14).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        sheet.Range(first, 2, row, 14).Style.NumberFormat.Format = Money;
        return row + 1;
    }

    private static void DetailTable(IXLWorksheet sheet, string[] headers, List<object?[]> rows, int amountColumn)
    {
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(TableRow, c + 1).Value = headers[c];
        }
        var header = sheet.Range(TableRow, 1, TableRow, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = HeaderFill;

        var row = TableRow + 1;
        foreach (var values in rows)
        {
            for (var c = 0; c < values.Length; c++)
            {
                if (values[c] is decimal amount)
                {
                    sheet.Cell(row, c + 1).Value = amount;
                }
                else
                {
                    sheet.Cell(row, c + 1).Value = values[c]?.ToString() ?? string.Empty;
                }
            }
            row++;
        }

        sheet.Cell(row, amountColumn - 1).Value = "TOTAL";
        sheet.Cell(row, amountColumn).FormulaA1 = $"SUM({XLHelper.GetColumnLetterFromNumber(amountColumn)}{TableRow + 1}:{XLHelper.GetColumnLetterFromNumber(amountColumn)}{Math.Max(row - 1, TableRow + 1)})";
        sheet.Range(row, amountColumn - 1, row, amountColumn).Style.Font.Bold = true;
        sheet.Range(TableRow + 1, amountColumn, row, amountColumn).Style.NumberFormat.Format = Money;
        sheet.Columns(1, headers.Length).AdjustToContents(TableRow, Math.Min(row, TableRow + 300), 8, 50);
        sheet.SheetView.FreezeRows(TableRow);
        if (rows.Count > 0)
        {
            sheet.Range(TableRow, 1, row - 1, headers.Length).SetAutoFilter();
        }
    }

    private static string MonthYear(DateOnly date) =>
        $"{Spanish.TextInfo.ToTitleCase(Spanish.DateTimeFormat.GetMonthName(date.Month))} {date.Year}";

    private static string ScopeTitle(string scope) => scope switch
    {
        ReconciliationScope.Income => "solo ingresos",
        ReconciliationScope.Expense => "solo egresos",
        _ => "ingresos y egresos",
    };

    public static string StatusText(string status) => status switch
    {
        MatchStatus.Auto => "Cotejado",
        MatchStatus.Confirmed => "Cotejado (revisado)",
        MatchStatus.Manual => "Cotejado a mano",
        MatchStatus.Suggested => "Por revisar",
        LineStatus.MissingInLedger => "Falta en ASSINET",
        LineStatus.MissingInBank => "Falta en el banco",
        _ => status,
    };
}
