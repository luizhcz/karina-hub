using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Host.Api.Models.Responses;

public class AgentResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Tipo formal do agente. Sempre presente no JSON; <c>"Custom"</c> pra
    /// agentes sem template/tipo declarado.
    /// </summary>
    public required AgentType Type { get; init; }

    public required AgentModelConfig Model { get; init; }
    public AgentProviderConfig Provider { get; init; } = new();

    /// <summary>
    /// Texto cru autoral que o owner digitou. É o que o editor consome.
    /// O texto composto que vai pro LLM (<see cref="AgentDefinition.Instructions"/>)
    /// não é exposto via HTTP — vive apenas no snapshot pro runtime.
    /// </summary>
    public string? AuthorInstructions { get; init; }
    public IReadOnlyList<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public AgentOperationalMemoryDefinition? OperationalMemory { get; init; }
    public IReadOnlyList<AgentMiddlewareConfig> Middlewares { get; init; } = [];

    public ResiliencePolicy? Resilience { get; init; }

    public AgentCostBudget? CostBudget { get; init; }

    public IReadOnlyList<SkillRef>? SkillRefs { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>"project" (default) | "global".</summary>
    public required string Visibility { get; init; }

    /// <summary>Project owner do agent (distingue do caller que está consumindo).</summary>
    public required string OriginProjectId { get; init; }

    /// <summary>Tenant do owner. Reforça boundary cross-tenant na UI.</summary>
    public required string OriginTenantId { get; init; }

    /// <summary>
    /// Whitelist opcional de projetos autorizados a referenciar quando
    /// Visibility=global. Null = qualquer projeto do tenant pode.
    /// </summary>
    public IReadOnlyList<string>? AllowedProjectIds { get; init; }

    /// <summary>
    /// Quando false, runtime pula o agent em workflows que o referenciam.
    /// </summary>
    public bool Enabled { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Soft warnings da última validação (Router que descaracteriza template,
    /// MaxTokens alto, modelo full, etc). Null/omitido quando não há warnings.
    /// Não é estado persistido — é cálculo on-demand do <c>ValidateAsync</c>.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>
    /// IDs das intents do pool que este Router atende. Populado apenas quando
    /// <c>Type=Router</c>; null/omitido pra Custom. Resolvido on-demand a
    /// partir de <c>aihub.agent_router_intents</c>.
    /// </summary>
    public IReadOnlyList<string>? RouterIntentIds { get; init; }

    /// <summary>
    /// Gate "validated for chat" — populado pelo ChatSandboxService ao marcar
    /// uma session como Validated. Frontend usa pra renderizar badge/warnings
    /// no ChatDeployEditor sem ter regra de negócio próprio (decisão fica no backend).
    /// </summary>
    public DateTime? LastChatSandboxValidatedAt { get; init; }
    public string? LastChatSandboxValidatedByUserId { get; init; }
    public string? LastChatSandboxValidatedAgentVersionId { get; init; }

    public static AgentResponse FromDomain(
        AgentDefinition def,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? routerIntentIds = null) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Type = def.Type,
        Model = def.Model,
        Provider = def.Provider,
        AuthorInstructions = def.AuthorInstructions,
        Tools = def.Tools,
        StructuredOutput = def.StructuredOutput,
        OperationalMemory = def.OperationalMemory,
        Middlewares = def.Middlewares,
        Resilience = def.Resilience,
        CostBudget = def.CostBudget,
        SkillRefs = def.SkillRefs is { Count: > 0 } ? def.SkillRefs : null,
        Metadata = def.Metadata,
        Visibility = def.Visibility,
        OriginProjectId = def.ProjectId,
        OriginTenantId = def.TenantId,
        AllowedProjectIds = def.AllowedProjectIds,
        Enabled = def.Enabled,
        CreatedAt = def.CreatedAt,
        UpdatedAt = def.UpdatedAt,
        Warnings = warnings is { Count: > 0 } ? warnings : null,
        RouterIntentIds = routerIntentIds,
        LastChatSandboxValidatedAt = def.LastChatSandboxValidatedAt,
        LastChatSandboxValidatedByUserId = def.LastChatSandboxValidatedByUserId,
        LastChatSandboxValidatedAgentVersionId = def.LastChatSandboxValidatedAgentVersionId,
    };
}
