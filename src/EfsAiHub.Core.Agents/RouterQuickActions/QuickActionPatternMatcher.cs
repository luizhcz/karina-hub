using System.Text.RegularExpressions;

namespace EfsAiHub.Core.Agents.RouterQuickActions;

/// <summary>
/// Helpers puros (sem IO) pra normalização e match de patterns. Extraídos do
/// service de matching pra serem unit-testáveis em isolamento — toda a regra
/// determinística vive aqui.
///
/// <para>Sintaxe do pattern (validada no controller):</para>
/// <list type="bullet">
/// <item><c>"comprar petr4"</c> — match exato (após normalização).</item>
/// <item><c>"comprar *"</c> — prefixo + qualquer texto (1+ token).</item>
/// <item><c>"cotacao de *"</c> — 2 tokens fixos + 1+ token livre.</item>
/// </list>
///
/// <para>Match wins por especificidade: patterns com MAIS tokens fixos vencem.</para>
/// </summary>
public static class QuickActionPatternMatcher
{
    // Letras (incluindo acentuadas), dígitos, espaço, e '*' apenas no fim.
    // Sem regex complexa, sem caracteres especiais — proteção ReDoS por design.
    private static readonly Regex AllowedPatternChars = new(
        @"^[a-z0-9áéíóúâêîôûãõçñü ]+( \*)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Normaliza texto pra comparação: trim, lowercase invariant, e collapsed
    /// whitespace ("a  b   c" → "a b c"). NÃO remove acentos — preserva ASCII +
    /// PT-BR ("comprar petr4" e "Comprar PETR4" colidem, mas "açúcar" se mantém).
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lower = text.Trim().ToLowerInvariant();
        return string.Join(' ', lower.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// True se o pattern (já normalizado) é sintaticamente válido:
    /// caracteres permitidos, no máximo um '*' e sempre no final.
    /// </summary>
    public static bool IsValidPattern(string normalizedPattern) =>
        !string.IsNullOrEmpty(normalizedPattern)
        && AllowedPatternChars.IsMatch(normalizedPattern);

    /// <summary>
    /// Conta tokens fixos do pattern (ignorando o '*'). Usado pra desempate
    /// de especificidade — patterns mais específicos vencem ambiguidade.
    /// </summary>
    public static int Specificity(string normalizedPattern)
    {
        if (string.IsNullOrEmpty(normalizedPattern)) return 0;
        var trimmed = normalizedPattern.EndsWith(" *", StringComparison.Ordinal)
            ? normalizedPattern[..^2]
            : normalizedPattern;
        return trimmed.Length == 0 ? 0 : trimmed.Count(c => c == ' ') + 1;
    }

    /// <summary>True quando o pattern usa wildcard ('*' no fim).</summary>
    public static bool HasWildcard(string normalizedPattern) =>
        normalizedPattern.EndsWith(" *", StringComparison.Ordinal);

    /// <summary>
    /// True se o <paramref name="normalizedInput"/> bate no
    /// <paramref name="normalizedPattern"/>. Wildcard exige ≥1 token livre
    /// após o prefixo fixo — evita match degenerado (ex.: "comprar *" não
    /// bate em "comprar" sozinho).
    /// </summary>
    public static bool Matches(string normalizedPattern, string normalizedInput)
    {
        if (string.IsNullOrEmpty(normalizedInput) || string.IsNullOrEmpty(normalizedPattern))
            return false;

        if (!HasWildcard(normalizedPattern))
            return string.Equals(normalizedPattern, normalizedInput, StringComparison.Ordinal);

        // wildcard no fim: prefix + " " + qualquer token não-vazio
        var prefix = normalizedPattern[..^2];
        if (!normalizedInput.StartsWith(prefix + " ", StringComparison.Ordinal))
            return false;

        var rest = normalizedInput[(prefix.Length + 1)..];
        return rest.Length > 0;
    }
}
