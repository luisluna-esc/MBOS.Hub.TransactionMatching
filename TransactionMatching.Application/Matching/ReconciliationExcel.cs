using ClosedXML.Excel;
using TransactionMatching.Application.Dtos.Reconciliation;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>
/// El Excel de la conciliación, igual que el control que se hacía a mano ("1. BMSC 5642.xlsx"):
/// hojas INGRESO y EGRESO con las columnas del extracto a la izquierda, el importe del banco, la
/// diferencia, el importe de ASSINET con su fecha, comprobante y detalle a la derecha, y en amarillo
/// lo que falta registrar en un lado. Más una hoja RESUMEN con el cuadre de saldos.
/// </summary>
public static class ReconciliationExcel
{
    private static readonly string[] BankHeaders =
    [
        "Fecha", "Hora", "Cod. Bca.", "Nro.Cheque", "Nro/Nom.Plantilla", "Cod.Dep.Num", "Doc.Depositante",
        "Nombre/Denominación Depositante", "Tipo transact", "Descripción", "Oficina", "Banco", "Tipo dep",
        "Nom.Destinatario", "Glosa", "Originador", "Originador ACH", "Ciudad Origen",
    ];

    private static readonly XLColor Missing = XLColor.FromHtml("#FFFF00");
    private static readonly XLColor Review = XLColor.FromHtml("#FCE4D6");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1D3456");
    private const string Money = "#,##0.00";

    public static byte[] Build(Reconciliation reconciliation, IReadOnlyList<ReconLine> lines)
    {
        using var workbook = new XLWorkbook();
        var income = ReconciliationLines.Summary(lines, ReconciliationSide.Income);
        var expense = ReconciliationLines.Summary(lines, ReconciliationSide.Expense);

        SummarySheet(workbook.AddWorksheet("RESUMEN"), reconciliation, income, expense);
        SideSheet(workbook.AddWorksheet("INGRESO"), lines.Where(l => l.Side == ReconciliationSide.Income).ToList(),
            "CRÉDITO - FALTA DE REGISTRO ASSINET", "DÉBITO - FALTA DE REGISTRO BANCO");
        SideSheet(workbook.AddWorksheet("EGRESO"), lines.Where(l => l.Side == ReconciliationSide.Expense).ToList(),
            "DÉBITO - FALTA DE REGISTRO ASSINET", "CRÉDITO - FALTA DE REGISTRO BANCO");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SideSheet(IXLWorksheet sheet, List<ReconLine> lines, string bankHeader, string ledgerHeader)
    {
        // A-R: extracto | S: banco | T: diferencia | U: ASSINET | V: fecha | W: comprobante | X: detalle | Y: estado | Z: observación
        var headers = BankHeaders.Concat([bankHeader, "DIFERENCIA", ledgerHeader, "Fecha", "COMPROBANTE", "DETALLE", "ESTADO", "OBSERVACIÓN"]).ToArray();
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }
        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = HeaderFill;
        header.Style.Alignment.WrapText = true;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var row = 2;
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

            sheet.Cell(row, 20).FormulaA1 = $"S{row}-U{row}";

            if (line.Ledger is { } l)
            {
                sheet.Cell(row, 21).Value = line.LedgerAmount;
                sheet.Cell(row, 22).Value = l.AccountingDate.ToString("d/MM/yyyy");
                sheet.Cell(row, 23).Value = l.Batch ?? string.Empty;
                sheet.Cell(row, 24).Value = l.Description;
            }

            sheet.Cell(row, 25).Value = StatusText(line.Status);
            sheet.Cell(row, 26).Value = string.Join(" · ", new[] { line.Match?.Note, line.Bank?.Note, line.Ledger?.Note }.Where(n => !string.IsNullOrWhiteSpace(n)));

            if (line.IsMissing)
            {
                sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = Missing;
            }
            else if (line.Status == MatchStatus.Suggested || line.Difference != 0)
            {
                sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = Review;
            }
            row++;
        }

        // Totales.
        sheet.Cell(row, 18).Value = "TOTAL";
        sheet.Cell(row, 19).FormulaA1 = $"SUM(S2:S{row - 1})";
        sheet.Cell(row, 20).FormulaA1 = $"S{row}-U{row}";
        sheet.Cell(row, 21).FormulaA1 = $"SUM(U2:U{row - 1})";
        sheet.Range(row, 18, row, 21).Style.Font.Bold = true;
        sheet.Range(row, 18, row, 21).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        sheet.Range(2, 19, row, 21).Style.NumberFormat.Format = Money;
        sheet.Columns(1, headers.Length).AdjustToContents(1, Math.Min(row, 200), 8, 45);
        sheet.Column(10).Width = 50;
        sheet.Column(24).Width = 45;
        sheet.Row(1).Height = 32;
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length).SetAutoFilter();
    }

    private static void SummarySheet(IXLWorksheet sheet, Reconciliation r, SideSummaryDto income, SideSummaryDto expense)
    {
        var reconciled = ReconciliationLines.ReconciledBalance(r.LedgerClosingBalance, income, expense);
        var account = r.BankAccount!;

        sheet.Cell("A1").Value = account.Department is null ? $"Conciliación bancaria {account.Name}" : $"Conciliación bancaria {account.Department} · {account.Name}";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 14;
        sheet.Cell("A2").Value = $"{account.Bank} · cuenta {account.AccountNumber}";
        sheet.Cell("A3").Value = $"Del {r.PeriodStart:dd/MM/yyyy} al {r.PeriodEnd:dd/MM/yyyy}";

        var row = 5;
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

        row++;
        sheet.Cell(row, 1).Value = "";
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

    private static string StatusText(string status) => status switch
    {
        MatchStatus.Auto => "Cotejado",
        MatchStatus.Confirmed => "Cotejado (revisado)",
        MatchStatus.Manual => "Cotejado a mano",
        MatchStatus.Suggested => "Por revisar",
        LineStatus.MissingInLedger => "Falta registro ASSINET",
        LineStatus.MissingInBank => "Falta registro banco",
        _ => status,
    };
}
