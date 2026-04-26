namespace EfsAiHub.Platform.Runtime.Guards.Validators;

/// <summary>
/// Validador Mod11 para CPF (11 dígitos) e CNPJ (14 dígitos).
/// Roda pós-match em regex de PII pra eliminar falsos positivos
/// (qualquer sequência de 11 dígitos parece CPF mas só ~10% são válidos).
/// </summary>
public static class Mod11Validator
{
    public static bool IsValid(string raw)
    {
        var digits = ExtractDigits(raw);
        return digits.Length switch
        {
            11 => IsValidCpf(digits),
            14 => IsValidCnpj(digits),
            _ => false
        };
    }

    private static bool IsValidCpf(string d)
    {
        // Sequências repetidas (00000000000, 11111111111…) passam no algoritmo
        // mas são notoriamente falsos positivos — recusa explícita.
        if (AllSameDigit(d)) return false;

        var digit1 = ComputeCpfDigit(d, length: 9);
        if (digit1 != d[9] - '0') return false;

        var digit2 = ComputeCpfDigit(d, length: 10);
        return digit2 == d[10] - '0';
    }

    private static int ComputeCpfDigit(string d, int length)
    {
        var sum = 0;
        var weight = length + 1;
        for (var i = 0; i < length; i++)
            sum += (d[i] - '0') * weight--;

        // Forma equivalente à clássica "mod = sum%11; dv = mod<2 ? 0 : 11-mod":
        // (sum*10) mod 11 == (-sum) mod 11 == 11-(sum mod 11) quando sum mod 11 != 0.
        // Validado contra CPFs sintéticos 52998224725 (dv1=2,dv2=5) e 11144477735 (dv1=3,dv2=5).
        var mod = (sum * 10) % 11;
        return mod == 10 ? 0 : mod;
    }

    private static bool IsValidCnpj(string d)
    {
        if (AllSameDigit(d)) return false;

        var weights1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var weights2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var digit1 = ComputeCnpjDigit(d, weights1, length: 12);
        if (digit1 != d[12] - '0') return false;

        var digit2 = ComputeCnpjDigit(d, weights2, length: 13);
        return digit2 == d[13] - '0';
    }

    private static int ComputeCnpjDigit(string d, int[] weights, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += (d[i] - '0') * weights[i];

        var mod = sum % 11;
        return mod < 2 ? 0 : 11 - mod;
    }

    private static string ExtractDigits(string raw)
    {
        Span<char> buf = stackalloc char[raw.Length];
        var n = 0;
        foreach (var c in raw)
            if (c >= '0' && c <= '9') buf[n++] = c;
        return new string(buf[..n]);
    }

    private static bool AllSameDigit(string d)
    {
        for (var i = 1; i < d.Length; i++)
            if (d[i] != d[0]) return false;
        return true;
    }
}
