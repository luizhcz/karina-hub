using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Host.Api.Models.Requests;

public class CreateAgentRequest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Tipo formal do agente. Custom (default) = sem template/validação;
    /// outros valores habilitam validações por tipo no save.
    /// </summary>
    public AgentType? Type { get; init; }

    public required AgentModelConfig Model { get; init; }
    public AgentProviderConfig Provider { get; init; } = new();
    public string? Instructions { get; init; }
    public List<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public AgentOperationalMemoryDefinition? OperationalMemory { get; init; }
    public List<AgentMiddlewareConfig> Middlewares { get; init; } = [];

    /// <summary>Política de retry/backoff. Null = defaults do engine.</summary>
    public ResiliencePolicy? Resilience { get; init; }

    /// <summary>Teto de custo em USD por execução. Null = sem enforcement.</summary>
    public AgentCostBudget? CostBudget { get; init; }

    /// <summary>Skills referenciadas (id + versão opcional). Null ou vazio = sem skills.</summary>
    public List<SkillRef>? SkillRefs { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// "project" (default) | "global". Quando ausente em UPDATE, AgentService preserva
    /// existing.Visibility — não cair pra default "project".
    /// </summary>
    public string? Visibility { get; init; }

    /// <summary>
    /// Whitelist opcional de projetos autorizados a referenciar este agent
    /// quando Visibility=global. Null = qualquer projeto do tenant.
    /// </summary>
    public List<string>? AllowedProjectIds { get; init; }

    /// <summary>
    /// Intent declarado pelo owner ao publicar o snapshot resultante deste upsert.
    /// <c>true</c> = workflow caller pinado em ancestor desta version não recebe
    /// patch propagation (fica preso no pin). <c>false</c> (default) = patch (propaga).
    /// <c>BreakingChange=true</c> exige <c>ChangeReason</c> não-vazio (validado em
    /// <c>AgentVersion.EnsureInvariants</c>).
    /// </summary>
    public bool BreakingChange { get; init; }

    /// <summary>
    /// Justificativa da mudança — obrigatória quando <c>BreakingChange=true</c>.
    /// Usada por callers pra decidir migração de pin.
    /// </summary>
    public string? ChangeReason { get; init; }

    /// <summary>
    /// Set de intenções que este Router atende (pool global do tenant —
    /// <c>aihub.router_intents</c>). Aplicável apenas quando <c>Type=Router</c>;
    /// ignorado pra Custom. Quando ausente em UPDATE, o serviço preserva o
    /// set existente. Em CREATE, ausente = vazio (Router rejeitado em
    /// validate por count &lt; 2).
    /// </summary>
    public List<string>? RouterIntentIds { get; init; }

    public AgentDefinition ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Type = Type ?? AgentType.Custom,
        Model = Model,
        Provider = Provider,
        Instructions = Instructions,
        Tools = Tools,
        StructuredOutput = StructuredOutput,
        OperationalMemory = OperationalMemory,
        Middlewares = Middlewares,
        Resilience = Resilience,
        CostBudget = CostBudget,
        SkillRefs = SkillRefs ?? [],
        Metadata = Metadata,
        // Default "project" pra Create; Update preserva existing via AgentService.
        Visibility = Visibility ?? "project",
        AllowedProjectIds = AllowedProjectIds is null ? null : (IReadOnlyList<string>)AllowedProjectIds,
        RouterIntentIds = RouterIntentIds is null ? null : (IReadOnlyList<string>)RouterIntentIds,
    };
}
