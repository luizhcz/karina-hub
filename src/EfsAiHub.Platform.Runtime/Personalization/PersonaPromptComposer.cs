using System.Diagnostics;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Agents.Execution;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Platform.Runtime.Execution;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Platform.Runtime.Personalization;

/// <summary>
/// Implementação default do <see cref="IPersonaPromptComposer"/>.
///
/// Resolve template (cache L1 → Redis → PG) na cadeia de 5 níveis (F4 /
/// ADR 003):
///   1. <c>project:{projectId}:agent:{agentId}:{userType}</c>  (mais específico)
///   2. <c>project:{projectId}:{userType}</c>
///   3. <c>agent:{agentId}:{userType}</c>
///   4. <c>global:{userType}</c>
///   5. null (persona fica sem bloco)
///
/// ProjectId foi escolhido como boundary de isolamento em vez de TenantId
/// porque já é enforced em 6 entidades via <c>HasQueryFilter</c>, e projects
/// já pertencem a tenants via <c>projects.tenant_id</c>. Ver ADR 003.
///
/// F6 — A/B testing: quando um <see cref="PersonaPromptExperiment"/> está
/// ativo para (<paramref name="projectId"/>, scope_resolvido), o composer
/// faz bucketing determinístico por <c>persona.UserId</c> e usa o
/// <see cref="PersonaPromptTemplateVersion"/> da variant escolhida no lugar
/// da ActiveVersionId corrente. O <see cref="ExperimentAssignment"/> vai pro
/// <see cref="ExecutionContext.ExperimentAssignments"/> pro TokenTrackingChatClient
/// persistir em llm_token_usage.
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
    private readonly IPersonaPromptExperimentRepository? _experiments;
    private readonly IPersonaPromptTemplateRepository? _templateRepo;
    private readonly ILogger<PersonaPromptComposer> _logger;

    public PersonaPromptComposer(
        IPersonaPromptTemplateCache cache,
        IPersonaPromptExperimentRepository? experiments = null,
        IPersonaPromptTemplateRepository? templateRepo = null,
        ILogger<PersonaPromptComposer>? logger = null)
    {
        _cache = cache;
        _experiments = experiments;
        _templateRepo = templateRepo;
        _logger = logger ?? NullLogger<PersonaPromptComposer>.Instance;
    }

    public async Task<ComposedPersonaPrompt> ComposeAsync(
        UserPersona? persona,
        string? agentId,
        string? projectId = null,
        CancellationToken ct = default)
    {
        if (persona is null || persona.IsAnonymous)
            return ComposedPersonaPrompt.Empty;

        var resolvedNullable = await ResolveTemplateAsync(agentId, persona.UserType, projectId, ct);
        if (resolvedNullable is not { } resolved)
            return ComposedPersonaPrompt.Empty;

        // F6 — check active experiment para (projectId, scope resolvido).
        // Só tenta quando temos os 3 componentes (experiments injetado + projectId
        // + template repo pra resolver version snapshot).
        var templateContent = resolved.Template.Template;
        if (_experiments is not null && _templateRepo is not null
            && !string.IsNullOrWhiteSpace(projectId))
        {
            var experiment = await _experiments.GetActiveAsync(projectId!, resolved.Scope, ct);
            if (experiment is not null)
            {
                var variant = ExperimentAssignment.AssignVariant(
                    persona.UserId, experiment.Id, experiment.TrafficSplitB);
                var chosenVersionId = variant == 'B'
                    ? experiment.VariantBVersionId
                    : experiment.VariantAVersionId;

                var versionRow = await _templateRepo.GetVersionByIdAsync(chosenVersionId, ct);
                if (versionRow is not null)
                {
                    templateContent = versionRow.Template;
                    var assignment = new ExperimentAssignment(
                        experiment.Id, variant, chosenVersionId);
                    RecordAssignment(agentId ?? "(global)", assignment);
                }
                else
                {
                    // VersionId órfã — version deletada direto no DB. Composer
                    // degrada pro ActiveVersionId corrente pra não quebrar o hot
                    // path, mas sinaliza em log + métrica + span pra alertar
                    // que o experiment virou zumbi (nenhum sample vai entrar
                    // pra essa variant até ser corrigido).
                    _logger.LogWarning(
                        "[PersonaExperiment] Variant órfã detectada: experimentId={ExperimentId} variant={Variant} versionId={VersionId}. Degradando pro ActiveVersionId do template.",
                        experiment.Id, variant, chosenVersionId);
                    MetricsRegistry.PersonaExperimentOrphanedVariants.Add(1,
                        new KeyValuePair<string, object?>("experiment_id", experiment.Id));
                    Activity.Current?.AddEvent(new ActivityEvent(
                        "persona.experiment.variant_orphaned",
                        tags: new ActivityTagsCollection
                        {
                            { "persona.experiment.id", experiment.Id },
                            { "persona.experiment.variant", variant.ToString() },
                            { "persona.experiment.version_id", chosenVersionId.ToString() },
                        }));
                }
            }
        }

        var rendered = PersonaTemplateRenderer.Render(templateContent, persona);
        if (string.IsNullOrWhiteSpace(rendered))
            return ComposedPersonaPrompt.Empty;

        // Observability: inchaço no template se reflete aqui — histogram detecta.
        MetricsRegistry.PersonaPromptComposeChars.Record(rendered.Length,
            new KeyValuePair<string, object?>("user_type", persona.UserType));

        return new ComposedPersonaPrompt(
            SystemSection: rendered,
            UserReinforcement: BuildUserReinforcement(persona));
    }

    private static void RecordAssignment(string agentId, ExperimentAssignment assignment)
    {
        // Grava no ExecutionContext corrente pra TokenTrackingChatClient ler no write.
        var ctx = DelegateExecutor.Current.Value;
        if (ctx?.ExperimentAssignments is not null)
        {
            ctx.ExperimentAssignments[agentId] = assignment;
        }

        // Tags no span LLMCall pai — útil em troubleshoot ad-hoc.
        var activity = Activity.Current;
        activity?.SetTag("persona.experiment.id", assignment.ExperimentId);
        activity?.SetTag("persona.experiment.variant", assignment.Variant.ToString());

        // Métrica em tempo real — não depender do batch writer do llm_token_usage.
        MetricsRegistry.PersonaExperimentAssignments.Add(1,
            new KeyValuePair<string, object?>("experiment_id", assignment.ExperimentId),
            new KeyValuePair<string, object?>("variant", assignment.Variant.ToString()));
    }

    private async Task<ResolvedTemplate?> ResolveTemplateAsync(
        string? agentId, string userType, string? projectId, CancellationToken ct)
    {
        var hasAgent = !string.IsNullOrWhiteSpace(agentId);
        var hasProject = !string.IsNullOrWhiteSpace(projectId);

        // Cadeia de 5 níveis (mais específico → mais genérico).
        if (hasProject && hasAgent)
        {
            var scope = PersonaPromptTemplate.ProjectAgentScope(projectId!, agentId!, userType);
            var tpl = await _cache.GetByScopeAsync(scope, ct);
            if (tpl is not null) return new ResolvedTemplate(tpl, scope);
        }
        if (hasProject)
        {
            var scope = PersonaPromptTemplate.ProjectGlobalScope(projectId!, userType);
            var tpl = await _cache.GetByScopeAsync(scope, ct);
            if (tpl is not null) return new ResolvedTemplate(tpl, scope);
        }
        if (hasAgent)
        {
            var scope = PersonaPromptTemplate.AgentScope(agentId!, userType);
            var tpl = await _cache.GetByScopeAsync(scope, ct);
            if (tpl is not null) return new ResolvedTemplate(tpl, scope);
        }
        var globalScope = PersonaPromptTemplate.GlobalScope(userType);
        var global = await _cache.GetByScopeAsync(globalScope, ct);
        return global is null ? null : new ResolvedTemplate(global, globalScope);
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

    private readonly record struct ResolvedTemplate(PersonaPromptTemplate Template, string Scope);
}
