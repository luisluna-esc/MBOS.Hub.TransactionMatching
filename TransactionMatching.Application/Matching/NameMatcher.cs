using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TransactionMatching.Core.Entities.Reconciliation;

namespace TransactionMatching.Application.Matching;

/// <summary>
/// Compara el nombre del banco ("LOPEZ PIZARROSO PAULA", "1028123025-EMPACAR SA") con el de ASSINET
/// ("Lopez Paula", "Empacar SA."). Cada palabra de ASSINET busca su palabra más parecida en el banco
/// (Jaro-Winkler, así "Wilfredso" ≈ "Wilfredo" y "Callizaya" ≈ "Callisaya") y se promedian.
/// </summary>
public static partial class NameMatcher
{
    /// <summary>Palabras que no identifican a nadie: tipos de movimiento, bancos, conectores.</summary>
    private static readonly HashSet<string> Noise =
    [
        "de", "del", "la", "las", "los", "el", "y", "e", "sa", "s", "a", "srl", "ltda", "vda",
        "credito", "debito", "transferencia", "transf", "ach", "qr", "bm", "cobros", "cobro", "servicios",
        "pago", "pagos", "deposito", "efectivo", "cheque", "propio", "banco", "bancos", "planilla", "aut",
        "proveedores", "fondos", "bolivia", "nacional", "union", "solidario", "ganadero", "economico",
        "fie", "bisa", "crecer", "ifd", "mercantil",
        // Banco Unión y BNB: "N/C TRASP. DESDE ...", "DEPOSITO A CUENTA", "DEB.CTA.P/ACH".
        "nc", "nd", "trasp", "traspaso", "desde", "hacia", "entre", "por", "cuenta", "cta", "deb", "comision", "net",
    ];

    /// <summary>Las palabras del nombre en el banco: depositante, originador y destinatario; si no hay, la descripción.</summary>
    public static List<string> BankTokens(BankMovement movement)
    {
        var tokens = Tokens(string.Join(' ', new[] { movement.DepositorName, movement.OriginatorAch, movement.RecipientName }
            .Where(text => !string.IsNullOrWhiteSpace(text))));
        return tokens.Count > 0 ? tokens : Tokens($"{movement.Description} {movement.Glosa}");
    }

    public static List<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
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
        return NonLetters().Split(builder.ToString())
            .Where(word => word.Length > 1 && !Noise.Contains(word))
            .Distinct()
            .ToList();
    }

    /// <summary>0 (nada en común) a 1 (todas las palabras de ASSINET están en el banco).</summary>
    public static double Similarity(IReadOnlyList<string> bankTokens, IReadOnlyList<string> ledgerTokens)
    {
        if (bankTokens.Count == 0 || ledgerTokens.Count == 0)
        {
            return 0;
        }

        // También las palabras del banco pegadas de a dos: "ada luz" contra "adaluz".
        var targets = bankTokens.Concat(bankTokens.Zip(bankTokens.Skip(1), (a, b) => a + b)).ToList();

        var total = 0.0;
        foreach (var word in ledgerTokens)
        {
            var best = targets.Max(target => JaroWinkler(word, target));
            // Por debajo de 0.85 dos palabras ya son distintas ("Rojas" y "Rosa"): no suman.
            total += best >= 0.85 ? best : 0;
        }
        return total / ledgerTokens.Count;
    }

    public static double JaroWinkler(string a, string b)
    {
        if (a == b)
        {
            return 1;
        }
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var range = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;
        for (var i = 0; i < a.Length; i++)
        {
            for (int j = Math.Max(0, i - range), end = Math.Min(b.Length, i + range + 1); j < end; j++)
            {
                if (!bMatched[j] && a[i] == b[j])
                {
                    aMatched[i] = bMatched[j] = true;
                    matches++;
                    break;
                }
            }
        }
        if (matches == 0)
        {
            return 0;
        }

        var transpositions = 0;
        for (int i = 0, k = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }
            while (!bMatched[k])
            {
                k++;
            }
            if (a[i] != b[k])
            {
                transpositions++;
            }
            k++;
        }

        double m = matches;
        var jaro = (m / a.Length + m / b.Length + (m - transpositions / 2.0) / m) / 3;
        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
        {
            prefix++;
        }
        return jaro + prefix * 0.1 * (1 - jaro);
    }

    [GeneratedRegex("[^a-z]+")]
    private static partial Regex NonLetters();
}
