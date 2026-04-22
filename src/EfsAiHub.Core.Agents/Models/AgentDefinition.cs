using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents;

public class AgentDefinition
{
    public string ProjectId { get; set; } = "default";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required AgentModelConfig Model { get; init; }

    /// <summary>
    /// Configuração do provider de LLM.
    /// Se omitido, usa AzureFoundry com os defaults de AzureAIOptions.
    /// </summary>
    public AgentProviderConfig Provider { get; init; } = new();

    public string? Instructions { get; init; }
    public IReadOnlyList<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public IReadOnlyList<AgentMiddlewareConfig> Middlewares { get; init; } = [];

    /// <summary>
    /// Item 9 — provider de fallback para circuit breaker. Null = sem failover (throw CircuitOpenException).
    /// Deve ser de tipo DIFERENTE do Provider.Type para ter efeito (R3).
    /// </summary>
    public AgentProviderConfig? FallbackProvider { get; init; }

    /// <summary>Fase 2 — política de retry/backoff por agente. Null = defaults.</summary>
    public ResiliencePolicy? Resilience { get; init; }

    /// <summary>Fase 2 — orçamento de custo em USD por execução. Null = sem enforcement de custo.</summary>
    public AgentCostBudget? CostBudget { get; init; }

    /// <summary>
    /// Fase 3 — skills (agrupamentos de tools+addendum+policy) referenciadas pelo agente.
    /// Resolvidas pelo AgentFactory antes de BuildAgentOptions: tools mescladas às tools flat
    /// existentes e addenda concatenados ao prompt final.
    /// </summary>
    public IReadOnlyList<SkillRef> SkillRefs { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Factory method validante. Única forma correta de construir em código imperativo.
    /// Para deserialização, use <c>new AgentDefinition { ... }</c> + <see cref="EnsureInvariants"/>.
    /// </summary>
    /// <exception cref="DomainException">Se alguma invariante for violada.</exception>
    public static AgentDefinition Create(
        string id,
        string name,
        AgentModelConfig model,
        string? instructions = null,
        string? description = null,
        AgentProviderConfig? provider = null,
        IReadOnlyList<AgentToolDefinition>? tools = null,
        IReadOnlyList<AgentMiddlewareConfig>? middlewares = null,
        IReadOnlyList<SkillRef>? skillRefs = null,
        AgentStructuredOutputDefinition? structuredOutput = null,
        AgentProviderConfig? fallbackProvider = null,
        ResiliencePolicy? resilience = null,
        AgentCostBudget? costBudget = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string projectId = "default")
    {
        var agent = new AgentDefinition
        {
            Id = id,
            Name = name,
            Model = model,
            Instructions = instructions,
            Description = description,
            Provider = provider ?? new(),
            Tools = tools ?? [],
            Middlewares = middlewares ?? [],
            SkillRefs = skillRefs ?? [],
            StructuredOutput = structuredOutput,
            FallbackProvider = fallbackProvider,
            Resilience = resilience,
            CostBudget = costBudget,
            Metadata = metadata ?? new Dictionary<string, string>(),
            ProjectId = projectId
        };
        agent.EnsureInvariants();
        return agent;
    }

    /// <summary>
    /// Valida invariantes e lança <see cref="DomainException"/> se violadas. Idempotente.
    /// </summary>
    /// <exception cref="DomainException">Se alguma invariante for violada.</exception>
    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new DomainException("AgentDefinition.Id é obrigatório.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("AgentDefinition.Name é obrigatório.");
        if (Model is null || string.IsNullOrWhiteSpace(Model.DeploymentName))
            throw new DomainException("AgentDefinition.Model.DeploymentName é obrigatório.");
        if (Model.Temperature is < 0 or > 2)
            throw new DomainException("AgentDefinition.Model.Temperature deve estar em [0, 2] quando presente.");
    }
}

public class AgentProviderConfig
{
    /// <summary>
    /// Provider de LLM a usar.
    /// "AzureFoundry" (default) | "AzureOpenAI" | "OpenAI"
    /// </summary>
    public string Type { get; init; } = "AzureFoundry";

    /// <summary>
    /// Subtipo de cliente dentro do provider.
    /// "ChatCompletion" (default) | "Responses" | "Assistants"
    /// Ignorado para AzureFoundry (usa sempre PersistentAgentsClient).
    /// </summary>
    public string ClientType { get; init; } = "ChatCompletion";

    /// <summary>
    /// Endpoint do Azure OpenAI: "https://&lt;resource&gt;.openai.azure.com"
    /// Se omitido, usa AzureAIOptions.Endpoint da configuração global.
    /// Ignorado para provider OpenAI.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// API Key para OpenAI (obrigatório) ou Azure OpenAI (opcional — prefira credencial gerenciada).
    /// Para AzureFoundry ou AzureOpenAI sem apiKey, usa DefaultAzureCredential.
    /// </summary>
    public string? ApiKey { get; init; }
}

public class AgentModelConfig
{
    public required string DeploymentName { get; set; }
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public class AgentToolDefinition
{
    /// <summary>"code_interpreter" | "file_search" | "function" | "mcp" | "web_search"</summary>
    public required string Type { get; init; }

    // function tool fields
    public string? Name { get; init; }
    public bool RequiresApproval { get; init; } = false;

    /// <summary>
    /// Fase 6 — fingerprint (sha256 canônico de <c>{Name, Description, JsonSchema}</c>)
    /// da versão da tool esperada no momento do snapshot do agente. Populado em
    /// <c>PgAgentDefinitionRepository.UpsertAsync</c>. O <c>ChatOptionsBuilder</c>
    /// resolve por este hash (fail-fast quando a tool evoluiu, salvo feature flag
    /// <c>AllowToolFingerprintMismatch</c>).
    /// </summary>
    public string? FingerprintHash { get; init; }

    // mcp tool fields
    /// <summary>
    /// Referência por Id ao registro em <c>aihub.mcp_servers</c>. Quando presente, o provider
    /// LLM resolve <c>ServerLabel</c>, <c>ServerUrl</c>, <c>AllowedTools</c> e <c>Headers</c>
    /// em runtime a partir do registro — mudanças no MCP server propagam automaticamente.
    /// Se null, cai no fallback legacy (campos inline abaixo) para BC com agents seedados.
    /// </summary>
    public string? McpServerId { get; init; }

    /// <summary>Legacy/fallback: label do server MCP (preencher só se <see cref="McpServerId"/> não existir).</summary>
    public string? ServerLabel { get; init; }
    /// <summary>Legacy/fallback: URL inline do server MCP.</summary>
    public string? ServerUrl { get; init; }
    /// <summary>Legacy/fallback: whitelist inline das tools MCP permitidas.</summary>
    public List<string> AllowedTools { get; init; } = [];

    /// <summary>"never" | "always" — apenas para MCP tools</summary>
    public string? RequireApproval { get; init; }
    /// <summary>Legacy/fallback: headers inline para o MCP server.</summary>
    public Dictionary<string, string> Headers { get; init; } = [];

    // web_search (Bing Grounding) fields — connectionId do Azure AI Foundry
    public string? ConnectionId { get; init; }
}

/// <summary>
/// Configuração de middleware personalizado por agente.
/// Middlewares são aplicados como DelegatingChatClient na pipeline LLM do agente.
/// </summary>
public class AgentMiddlewareConfig
{
    /// <summary>
    /// Tipo do middleware: "AccountGuard" | (extensível para futuros tipos)
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Ativar/desativar sem remover a configuração.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Configuração específica do middleware (formato varia por Type).</summary>
    public Dictionary<string, string> Settings { get; init; } = [];
}

public class AgentStructuredOutputDefinition
{
    /// <summary>"text" | "json" | "json_schema"</summary>
    public string ResponseFormat { get; init; } = "text";
    public string? SchemaName { get; init; }
    public string? SchemaDescription { get; init; }

    /// <summary>JSON Schema raw — repassado diretamente ao framework via ChatResponseFormat.ForJsonSchema()</summary>
    public JsonDocument? Schema { get; init; }
}
