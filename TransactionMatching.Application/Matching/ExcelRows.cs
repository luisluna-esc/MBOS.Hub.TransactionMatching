using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace TransactionMatching.Application.Matching;

/// <summary>
/// Lee la primera hoja de un .xls o .xlsx como filas de celdas (sin tipos), y convierte textos
/// del banco y de ASSINET a fechas y números.
/// </summary>
internal static partial class ExcelRows
{
    static ExcelRows()
    {
        // El .xls del banco viene en Windows-1252: .NET no trae esa codificación por defecto.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static List<object?[]> FirstSheet(byte[] content) => Sheets(content)[0];

    /// <summary>Todas las hojas, en orden (el extracto del BMSC trae además "Reporte de Pagos").</summary>
    public static List<List<object?[]>> Sheets(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sheets = new List<List<object?[]>>();
        do
        {
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.GetValue(i);
                }
                rows.Add(row);
            }
            sheets.Add(rows);
        }
        while (reader.NextResult());
        return sheets;
    }

    public static string? Text(object?[] row, int index)
    {
        if (index < 0 || index >= row.Length || row[index] is null)
        {
            return null;
        }
        var text = row[index] is DateTime date ? date.ToString("dd/MM/yyyy", Invariant) : Convert.ToString(row[index], Invariant);
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>"06/10/2026", "6/10/2026", "01/10/26" (año de 2 dígitos = 20xx) o una fecha de Excel.</summary>
    public static DateOnly? Date(object? value)
    {
        switch (value)
        {
            case DateTime dateTime:
                return DateOnly.FromDateTime(dateTime);
            case double serial:
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            case string text:
                var match = DatePattern().Match(text);
                if (match.Success && DateOnly.TryParseExact(
                        $"{int.Parse(match.Groups[1].Value):00}/{int.Parse(match.Groups[2].Value):00}/{FullYear(match.Groups[3].Value)}",
                        "dd/MM/yyyy", Invariant, DateTimeStyles.None, out var date))
                {
                    return date;
                }
                return null;
            default:
                return null;
        }
    }

    public static TimeOnly? Time(object? value) => value switch
    {
        DateTime dateTime => TimeOnly.FromDateTime(dateTime),
        double fraction => TimeOnly.FromTimeSpan(TimeSpan.FromDays(fraction % 1)),
        string text when TimeOnly.TryParse(text.Trim(), Invariant, out var time) => time,
        _ => null,
    };

    /// <summary>El banco manda los importes como texto con coma de miles: " 1,234.00".</summary>
    public static decimal Amount(object? value)
    {
        switch (value)
        {
            case null:
                return 0m;
            case double number:
                return Math.Round((decimal)number, 2);
            case decimal number:
                return Math.Round(number, 2);
            default:
                var text = Convert.ToString(value, Invariant)?.Replace(",", "").Replace("Bs", "").Trim();
                return decimal.TryParse(text, NumberStyles.Number, Invariant, out var amount) ? Math.Round(amount, 2) : 0m;
        }
    }

    /// <summary>Sin tildes, en minúsculas y con espacios simples: para comparar encabezados.</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }
        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    /// <summary>"26" → "2026"; "2026" queda igual.</summary>
    private static string FullYear(string year) => year.Length == 2 ? $"20{year}" : year;

    [GeneratedRegex(@"(\d{1,2})/(\d{1,2})/(\d{4}|\d{2})(?!\d)")]
    public static partial Regex DatePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
