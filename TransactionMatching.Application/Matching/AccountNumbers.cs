namespace TransactionMatching.Application.Matching;

/// <summary>
/// Cada banco escribe el número de cuenta a su manera: Banco Unión manda "10000024770469" para la
/// cuenta 1-24770469 y el BNB "100-0327936" para la 1000327936. Dos números son la misma cuenta si
/// coinciden sus últimos 8 dígitos (en las 13 cuentas de ASEA-MBO no se repiten).
/// </summary>
public static class AccountNumbers
{
    private const int SignificantDigits = 8;

    public static string Digits(string number) => new(number.Where(char.IsDigit).ToArray());

    public static bool Same(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }
        var x = Digits(a);
        var y = Digits(b);
        if (x == y)
        {
            return true;
        }
        return x.Length >= SignificantDigits && y.Length >= SignificantDigits && x[^SignificantDigits..] == y[^SignificantDigits..];
    }
}
