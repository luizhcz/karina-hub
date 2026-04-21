using System.Text.Json;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents;

public class AgentDefinition : IProjectScoped
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
    public List<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public List<AgentMiddlewareConfig> Middlewares { get; init; } = [];

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
    public List<SkillRef> SkillRefs { get; init; } = [];

    public Dictionary<string, string> Metadata { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public string? ServerLabel { get; init; }
    public string? ServerUrl { get; init; }
    public List<string> AllowedTools { get; init; } = [];

    /// <summary>"never" | "always" — apenas para MCP tools</summary>
    public string? RequireApproval { get; init; }
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
