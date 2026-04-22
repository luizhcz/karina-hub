using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.Enrichment;
using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Core.Orchestration.Workflows;

public class WorkflowDefinition
{
    public string ProjectId { get; set; } = "default";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; } = "1.0.0";
    public required OrchestrationMode OrchestrationMode { get; init; }
    public required IReadOnlyList<WorkflowAgentReference> Agents { get; init; }

    /// <summary>
    /// Passos de código puro (sem LLM) para o modo Graph.
    /// Cada item referencia uma função registrada em ICodeExecutorRegistry.
    /// </summary>
    public IReadOnlyList<WorkflowExecutorStep> Executors { get; init; } = [];

    public IReadOnlyList<WorkflowEdge> Edges { get; init; } = [];

    /// <summary>
    /// Fase 7 — Regras de roteamento declarativas consumidas pelo
    /// <c>IEscalationRouter</c> quando um agente emite <c>AgentEscalationSignal</c>.
    /// </summary>
    public IReadOnlyList<RoutingRule> RoutingRules { get; init; } = [];

    public WorkflowConfiguration Configuration { get; init; } = new();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// "project" (default) — visível apenas dentro do projeto atual.
    /// "global" — visível em todos os projetos (catálogo compartilhado).
    /// </summary>
    public string Visibility { get; init; } = "project";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Factory method validante — única forma correta de construir um <see cref="WorkflowDefinition"/>
    /// com invariantes protegidas em código imperativo (controllers, application services, clones).
    /// Para deserialização JSON vinda de persistência (repositórios), use
    /// <c>new WorkflowDefinition { ... }</c> e chame <see cref="EnsureInvariants"/> explicitamente
    /// após o mapeamento se quiser validar.
    /// </summary>
    /// <exception cref="DomainException">Se alguma invariante for violada.</exception>
    public static WorkflowDefinition Create(
        string id,
        string name,
        OrchestrationMode orchestrationMode,
        IReadOnlyList<WorkflowAgentReference> agents,
        IReadOnlyList<WorkflowEdge>? edges = null,
        IReadOnlyList<WorkflowExecutorStep>? executors = null,
        IReadOnlyList<RoutingRule>? routingRules = null,
        WorkflowConfiguration? configuration = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string projectId = "default",
        string visibility = "project",
        string? description = null,
        string version = "1.0.0")
    {
        var def = new WorkflowDefinition
        {
            Id = id,
            Name = name,
            Description = description,
            Version = version,
            OrchestrationMode = orchestrationMode,
            Agents = agents,
            Edges = edges ?? [],
            Executors = executors ?? [],
            RoutingRules = routingRules ?? [],
            Configuration = configuration ?? new(),
            Metadata = metadata ?? new Dictionary<string, string>(),
            Visibility = visibility,
            ProjectId = projectId
        };
        def.EnsureInvariants();
        return def;
    }

    /// <summary>
    /// Valida invariantes do agregado e lança <see cref="DomainException"/> se violadas.
    /// Idempotente — chamar múltiplas vezes é seguro. Chamado automaticamente por <see cref="Create"/>;
    /// callers de deserialização podem invocar para revalidar após carregar do banco.
    /// </summary>
    /// <exception cref="DomainException">Se alguma invariante for violada.</exception>
    public void EnsureInvariants()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new DomainException("WorkflowDefinition.Id é obrigatório.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("WorkflowDefinition.Name é obrigatório.");

        // Modos não-Graph exigem ao menos 1 agente
        if (OrchestrationMode != OrchestrationMode.Graph && Agents.Count == 0)
            throw new DomainException(
                $"Modo {OrchestrationMode} exige ao menos um agente em Agents.");

        // Modo Graph exige ao menos 1 edge
        if (OrchestrationMode == OrchestrationMode.Graph && Edges.Count == 0)
            throw new DomainException("Modo Graph exige ao menos uma edge em Edges.");

        // Edges do Graph devem referenciar agentes/executores existentes.
        // Bridges automáticas (__bridge_*__) são injetadas pelo runtime, aqui são só a definição.
        if (OrchestrationMode == OrchestrationMode.Graph)
        {
            var agentIds = new HashSet<string>(Agents.Select(a => a.AgentId), StringComparer.OrdinalIgnoreCase);
            var execIds = new HashSet<string>(Executors.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            bool Exists(string? id) =>
                id is not null && (agentIds.Contains(id) || execIds.Contains(id));

            foreach (var edge in Edges)
            {
                if (edge.From is { Length: > 0 } from && !Exists(from))
                    throw new DomainException(
                        $"Edge referencia node de origem inexistente: '{from}'.");
                if (edge.To is { Length: > 0 } to && !Exists(to))
                    throw new DomainException(
                        $"Edge referencia node de destino inexistente: '{to}'.");
            }
        }
    }
}

public class WorkflowAgentReference
{
    public required string AgentId { get; init; }

    /// <summary>"manager" | "participant" — para GroupChat</summary>
    public string? Role { get; init; }

    /// <summary>Configuração HITL declarativa — pausa antes/depois da execução do agente.</summary>
    public NodeHitlConfig? Hitl { get; init; }
}

/// <summary>
/// Define um passo de código puro (sem LLM) para uso no modo Graph.
/// O campo FunctionName referencia uma função registrada em ICodeExecutorRegistry.
/// </summary>
public class WorkflowExecutorStep
{
    /// <summary>ID único do passo — usado nas arestas (WorkflowEdge.From / To).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Nome da função registrada em ICodeExecutorRegistry.
    /// Ex: "fetch_ativos", "search_web_batch", "save_csv_exec"
    /// </summary>
    public string FunctionName { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Configuração HITL declarativa — pausa antes/depois da execução do executor.</summary>
    public NodeHitlConfig? Hitl { get; init; }
}

public class WorkflowEdge
{
    /// <summary>ID do nó de origem (agente ou executor).</summary>
    public string? From { get; init; }

    /// <summary>ID do nó de destino (agente ou executor). Para FanOut usa Targets; para FanIn usa Sources.</summary>
    public string? To { get; init; }

    /// <summary>
    /// Condição de roteamento (substring case-insensitive no output do executor de origem).
    /// Usado nos tipos Conditional e como identificador nos casos Switch.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>Tipo de aresta. Default: Direct (retrocompatível).</summary>
    public WorkflowEdgeType EdgeType { get; init; } = WorkflowEdgeType.Direct;

    /// <summary>IDs dos destinos para FanOut (1→N paralelo).</summary>
    public List<string> Targets { get; init; } = [];

    /// <summary>IDs das origens para FanIn (N→1 barreira).</summary>
    public List<string> Sources { get; init; } = [];

    /// <summary>Casos para Switch (avaliados em ordem; primeiro match vence).</summary>
    public List<WorkflowSwitchCase> Cases { get; init; } = [];

    /// <summary>
    /// Controla qual input o(s) nó(s) destino recebem.
    /// null (default) = output do nó anterior (comportamento atual).
    /// "WorkflowInput" = input original do workflow (ExecutionContext.Input).
    /// Quando definido, o engine injeta um nó bridge automático entre source e target.
    /// </summary>
    public string? InputSource { get; init; }
}

/// <summary>Um caso dentro de um Switch edge.</summary>
public class WorkflowSwitchCase
{
    /// <summary>
    /// Substring a ser procurada no output do executor (case-insensitive).
    /// Null ou omitido = caso default.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>IDs dos executores/agentes a acionar quando este caso for satisfeito.</summary>
    public required List<string> Targets { get; init; }

    /// <summary>Se true, este caso é o default (usado quando nenhum outro case combina).</summary>
    public bool IsDefault { get; init; } = false;
}

public class WorkflowConfiguration
{
    public int? MaxRounds { get; init; }
    public int TimeoutSeconds { get; set; } = 300;
    public bool EnableHumanInTheLoop { get; init; } = false;

    /// <summary>"InMemory" | "Blob"</summary>
    public string CheckpointMode { get; init; } = "InMemory";
    public bool ExposeAsAgent { get; init; } = false;
    public string? ExposedAgentDescription { get; init; }

    /// <summary>
    /// "Standalone" (default) — input é string livre disparado por API.
    /// "Chat" — input é ChatTurnContext JSON com histórico + userId + conversationId.
    /// </summary>
    public string InputMode { get; init; } = "Standalone";

    /// <summary>
    /// Tamanho máximo da janela de histórico enviada ao workflow em modo Chat.
    /// Mensagens anteriores a contextClearedAt são sempre excluídas independente deste valor.
    /// </summary>
    public int MaxHistoryMessages { get; init; } = 20;

    /// <summary>
    /// Budget máximo de tokens para o histórico no ChatTurnContext.
    /// Quando definido, as mensagens mais antigas são removidas primeiro até caber no budget.
    /// Usa ChatMessage.TokenCount (com fallback: Content.Length / 4).
    /// null = sem limite de tokens (apenas MaxHistoryMessages é aplicado).
    /// </summary>
    public int? MaxHistoryTokens { get; init; }

    /// <summary>
    /// Limite de invocações de agentes por execução (handoffs + respostas).
    /// Protege contra loops infinitos de handoff que desperdiçam tokens.
    /// Default: 10 (ex: manager→boleta→manager→relatorio = 4 invocações).
    /// </summary>
    public int MaxAgentInvocations { get; set; } = 10;

    /// <summary>
    /// Hard cap de tokens LLM totais consumidos por execução (input + output, somados em todas as chamadas).
    /// Ao ultrapassar, a próxima chamada LLM lança BudgetExceededException e a execução é marcada como Failed.
    /// Default: 50000 tokens.
    /// </summary>
    public int MaxTokensPerExecution { get; init; } = 50000;

    /// <summary>
    /// Fase 2 — Hard cap de custo em USD por execução. Calculado a partir de
    /// <see cref="Domain.Observability.ModelPricing"/> no TokenTrackingChatClient.
    /// null ou ≤0 = sem enforcement de custo (mantém comportamento legado).
    /// </summary>
    public decimal? MaxCostUsdPerExecution { get; init; }

    /// <summary>
    /// IDs explícitos dos nós que produzem output final do workflow (Graph mode).
    /// Quando definido, sobrescreve a auto-detecção de end nodes baseada em arestas.
    /// Necessário quando há feedback loops (ex: post-processor → agente em caso de erro).
    /// </summary>
    public IReadOnlyList<string>? OutputNodes { get; init; }

    /// <summary>
    /// Limite de execuções simultâneas para este workflow.
    /// null → sem semáforo — executa imediatamente ao receber input (ilimitado).
    /// > 0  → semáforo dedicado: máximo N execuções simultâneas deste workflow.
    /// Reinicializar o processo recria os semáforos (novo limite é aplicado imediatamente).
    /// </summary>
    public int? MaxConcurrentExecutions { get; init; }

    /// <summary>
    /// Regras declarativas de enrichment aplicadas pelo executor <c>generic_enricher</c>.
    /// Cada regra faz match por <c>response_type</c> e aplica disclaimers determinísticos
    /// e/ou defaults extraídos do contexto. Null = nenhum enrichment configurado.
    /// </summary>
    public IReadOnlyList<EnrichmentRule>? EnrichmentRules { get; init; }
}
