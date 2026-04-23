using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Execution;

namespace EfsAiHub.Platform.Runtime.Personalization;

/// <summary>
/// Implementação default do <see cref="IPersonaPromptComposer"/>.
///
/// Resolve template (cache L1 → Redis → PG) na cadeia:
///   <c>agent:{agentId}:{userType}</c> → <c>global:{userType}</c> → null.
///
/// Renderização é <see cref="PersonaTemplateRenderer"/> — pure function,
/// delega a cada subtipo (<see cref="ClientPersona"/> / <see cref="AdminPersona"/>)
/// o mapeamento placeholder → valor via <see cref="UserPersona.GetPlaceholderValue"/>.
///
/// O <see cref="ComposedPersonaPrompt.UserReinforcement"/> é montado em C#
/// (hardcoded ≤15 tokens por tipo) — não entra no template porque é invariante
/// de design (ancoragem de last-token bias precisa ser enxuta).
/// </summary>
public sealed class PersonaPromptComposer : IPersonaPromptComposer
{
    private readonly IPersonaPromptTemplateCache _cache;

    public PersonaPromptComposer(IPersonaPromptTemplateCache cache) => _cache = cache;

    public async Task<ComposedPersonaPrompt> ComposeAsync(
        UserPersona? persona,
        string? agentId,
        CancellationToken ct = default)
    {
        if (persona is null || persona.IsAnonymous)
            return ComposedPersonaPrompt.Empty;

        var template = await ResolveTemplateAsync(agentId, persona.UserType, ct);
        if (template is null)
            return ComposedPersonaPrompt.Empty;

        var rendered = PersonaTemplateRenderer.Render(template.Template, persona);
        if (string.IsNullOrWhiteSpace(rendered))
            return ComposedPersonaPrompt.Empty;

        // Observability: inchaço no template se reflete aqui — histogram detecta.
        MetricsRegistry.PersonaPromptComposeChars.Record(rendered.Length,
            new KeyValuePair<string, object?>("user_type", persona.UserType));

        return new ComposedPersonaPrompt(
            SystemSection: rendered,
            UserReinforcement: BuildUserReinforcement(persona));
    }

    private async Task<PersonaPromptTemplate?> ResolveTemplateAsync(
        string? agentId, string userType, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            var agentScoped = await _cache.GetByScopeAsync(
                PersonaPromptTemplate.AgentScope(agentId, userType), ct);
            if (agentScoped is not null) return agentScoped;
        }
        return await _cache.GetByScopeAsync(
            PersonaPromptTemplate.GlobalScope(userType), ct);
    }

    // ≤15 tokens. Conteúdo varia por subtipo — pra cliente ancora suitability+segment
    // (o que mais influencia tom de recomendação); pra admin ancora partnerType
    // (decide capacidades e tom da resposta).
    private static string? BuildUserReinforcement(UserPersona persona) => persona switch
    {
        ClientPersona c => BuildClientReinforcement(c),
        AdminPersona a => BuildAdminReinforcement(a),
        _ => null,
    };

    private static string? BuildClientReinforcement(ClientPersona c)
    {
        var hasSuitability = !string.IsNullOrWhiteSpace(c.SuitabilityLevel);
        var hasSegment = !string.IsNullOrWhiteSpace(c.BusinessSegment);
        if (!hasSuitability && !hasSegment) return null;

        var parts = new List<string>(2);
        if (hasSuitability) parts.Add($"persona.suitability={c.SuitabilityLevel}");
        if (hasSegment) parts.Add($"persona.segment={c.BusinessSegment}");
        return $"[{string.Join(", ", parts)}]";
    }

    private static string? BuildAdminReinforcement(AdminPersona a)
    {
        var hasPartner = !string.IsNullOrWhiteSpace(a.PartnerType);
        if (!hasPartner && !a.IsWm) return null;

        var parts = new List<string>(2);
        if (hasPartner) parts.Add($"persona.partner={a.PartnerType}");
        if (a.IsWm) parts.Add("persona.wm=sim");
        return $"[{string.Join(", ", parts)}]";
    }
}
