namespace EfsAiHub.Platform.Runtime.Guards.Validators;

/// <summary>
/// Validador Luhn (algoritmo de checksum de cartão de crédito ISO/IEC 7812).
/// Aplicado pós-match em regex de financial.credit_card pra eliminar falsos positivos
/// (qualquer 16 dígitos parece cartão; só ~10% passam no Luhn).
/// </summary>
public static class LuhnValidator
{
    public static bool IsValid(string raw)
    {
        var sum = 0;
        var alternate = false;
        // Itera direita-pra-esquerda; dobra cada segundo dígito.
        for (var i = raw.Length - 1; i >= 0; i--)
        {
            var c = raw[i];
            if (c < '0' || c > '9') continue;
            var n = c - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return sum > 0 && sum % 10 == 0;
    }
}
