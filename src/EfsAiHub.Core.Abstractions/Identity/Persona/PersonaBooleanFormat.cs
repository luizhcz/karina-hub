using System.Globalization;

namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Formata booleans da persona ("sim"/"não" vs "yes"/"no") conforme
/// <see cref="CultureInfo.CurrentUICulture"/> setada pelo middleware
/// <c>RequestLocalizationMiddleware</c> no pipeline do Host.Api.
///
/// Design: stateless static, sem dependência de <c>IStringLocalizer</c>
/// pra não poluir <see cref="UserPersona.GetPlaceholderValue"/> com
/// parâmetro extra. <see cref="CultureInfo.CurrentUICulture"/> é um
/// AsyncLocal gerenciado pelo .NET — propaga por request naturally.
///
/// Se nenhum culture match, cai em pt-BR (default do produto, fixo).
/// Extensível: novas culturas adicionam entry no <c>switch</c>.
/// </summary>
public static class PersonaBooleanFormat
{
    /// <summary>Formata um <paramref name="value"/> como sim/não ou equivalente localizado.</summary>
    public static string Format(bool value)
    {
        var culture = CultureInfo.CurrentUICulture;
        return IsEnglish(culture)
            ? (value ? "yes" : "no")
            : (value ? "sim" : "não");
    }

    private static bool IsEnglish(CultureInfo culture)
    {
        // Cobre en, en-US, en-GB etc. TwoLetterISOLanguageName é "en" pra todos.
        return string.Equals(culture.TwoLetterISOLanguageName, "en",
            StringComparison.OrdinalIgnoreCase);
    }
}
