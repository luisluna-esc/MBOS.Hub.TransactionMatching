using System.Text.RegularExpressions;

namespace TransactionMatching.Application.Matching;

/// <summary>
/// El comprobante de ASSINET sale del "Lote" del Mayor: "MB - 53 1.10" es el comprobante MB 53,
/// línea 1.10 ("* MR - 1 28.1" → MR 1; el asterisco y la línea no cambian el comprobante).
/// Contabilidad busca por comprobante ("MB53") para sumar sus depósitos.
/// </summary>
public static partial class Vouchers
{
    /// <summary>"MB - 53 1.10" → "MB 53"; null si el lote no tiene ese formato.</summary>
    public static string? Of(string? batch)
    {
        var match = batch is null ? null : Pattern().Match(batch);
        return match is { Success: true } ? $"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}" : null;
    }

    /// <summary>Para comparar lo que escribe el usuario: "mb-53", "MB 53" y "MB53" dan "MB53".</summary>
    public static string Key(string? text) => NonAlphanumeric().Replace(text ?? string.Empty, string.Empty).ToUpperInvariant();

    /// <summary>¿El lote es de ese comprobante? (exacto: MB53 no es MB530).</summary>
    public static bool Matches(string? batch, string? voucher) =>
        string.IsNullOrWhiteSpace(voucher) || Key(Of(batch)) == Key(voucher);

    [GeneratedRegex(@"([A-Za-z]{1,4})\s*-\s*(\d+)")]
    private static partial Regex Pattern();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
