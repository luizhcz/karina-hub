using System.Text.RegularExpressions;
using EfsAiHub.Core.Abstractions.Identity.Persona;

namespace EfsAiHub.Platform.Runtime.Personalization;

/// <summary>
/// Faz string replace determinístico de placeholders <c>{{field}}</c> no template
/// usando os valores retornados por <see cref="UserPersona.GetPlaceholderValue"/>.
/// Cada subtipo (<see cref="ClientPersona"/> / <see cref="AdminPersona"/>) define
/// seu próprio mapeamento — o renderer fica agnóstico.
///
/// <para>Regras intencionais:</para>
/// <list type="bullet">
///   <item>Sem lógica condicional (<c>{{#if}}</c>): se precisar, escrever em
///         linguagem natural e deixar o LLM interpretar. Evita virar template engine.</item>
///   <item>Placeholder conhecido com valor null/empty → substituído por <c>""</c>.</item>
///   <item>Placeholder desconhecido → deixado intocado no texto (torna typos
///         visíveis no output ao invés de silenciosamente sumir).</item>
///   <item>Case-sensitive (<c>{{Segment}}</c> ≠ <c>{{segment}}</c>).</item>
///   <item>Booleanos renderizam <c>"sim"/"não"</c> (decisão do subtipo via
///         <see cref="UserPersona.GetPlaceholderValue"/>).</item>
///   <item>Listas renderizam CSV com <c>", "</c> (idem).</item>
/// </list>
/// Pure function — classe estática por clareza; sem deps, 100% testável.
/// </summary>
public static class PersonaTemplateRenderer
{
    // Chaves duplas + whitespace opcional + identificador word-chars + whitespace
    // opcional + chaves duplas. Aceita {{segment}} e {{ segment }} — tolera
    // indentação acidental do admin sem virar typo literal no output.
    // Não pegamos chaves aninhadas — escopo é string replace literal.
    private static readonly Regex PlaceholderPattern =
        new(@"\{\{\s*(\w+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Aplica o template à persona. Retorna null se <paramref name="template"/>
    /// for null/vazio/whitespace ou <paramref name="persona"/> for null/anonymous.
    /// </summary>
    public static string? Render(string? template, UserPersona? persona)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        if (persona is null || persona.IsAnonymous) return null;

        return PlaceholderPattern.Replace(template!, match =>
        {
            var key = match.Groups[1].Value;
            var value = persona.GetPlaceholderValue(key);
            // null = placeholder desconhecido → preserva literal pra expor typo.
            return value ?? match.Value;
        });
    }
}
