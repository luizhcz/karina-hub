using System.Text.Json;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents;

public class AgentDefinition
{
    public string ProjectId { get; set; } = "default";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Tipo formal do agente. Custom (default) = agente livre, sem template
    /// aplicado, sem validações específicas — cobre back-compat de agentes
    /// criados antes da tipologia. Outros tipos (Router, etc.) habilitam
    /// validações de invariantes e templates de criação.
    /// </summary>
    public AgentType Type { get; set; } = AgentType.Custom;

    /// <summary>
    /// Set de intents do pool global (<c>aihub.router_intents</c>) que este
    /// Router atende. Persiste no jsonb da row pra que snapshot e runtime
    /// leiam direto sem precisar consultar a junction <c>agent_router_intents</c>
    /// — a junction permanece como fonte de verdade pra UI de edição
    /// (checkboxes do wizard) e pra propagação inversa quando uma intent é
    /// editada/removida. Aplicável apenas pra <c>Type=Router</c>.
    /// </summary>
    public IReadOnlyList<string>? RouterIntentIds { get; set; }

    public required AgentModelConfig Model { get; init; }

    /// <summary>
    /// Configuração do provider de LLM.
    /// Se omitido, usa AzureFoundry com os defaults de AzureAIOptions.
    /// </summary>
    public AgentProviderConfig Provider { get; init; } = new();

    public string? Instructions { get; init; }
    public IReadOnlyList<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }

    /// <summary>
    /// Quando não-null, ativa memória operacional: schema é mergeado ao
    /// ResponseFormat enviado ao LLM e o middleware OperationalMemoryChatClient
    /// é injetado automaticamente no pipeline (sem entry em <see cref="Middlewares"/>).
    /// Persistência por (ProjectId, AgentId, ConversationId|ExecutionId).
    /// </summary>
    public AgentOperationalMemoryDefinition? OperationalMemory { get; init; }
    public IReadOnlyList<AgentMiddlewareConfig> Middlewares { get; init; } = [];

    /// <summary>
    /// Provider de fallback para circuit breaker. Null = sem failover (throw CircuitOpenException).
    /// Deve ser de tipo DIFERENTE do Provider.Type para ter efeito.
    /// </summary>
    public AgentProviderConfig? FallbackProvider { get; init; }

    /// <summary>Política de retry/backoff por agente. Null = defaults.</summary>
    public ResiliencePolicy? Resilience { get; init; }

    /// <summary>Orçamento de custo em USD por execução. Null = sem enforcement de custo.</summary>
    public AgentCostBudget? CostBudget { get; init; }

    /// <summary>
    /// Skills (agrupamentos de tools+addendum+policy) referenciadas pelo agente.
    /// Resolvidas pelo AgentFactory antes de BuildAgentOptions: tools mescladas às tools flat
    /// existentes e addenda concatenados ao prompt final.
    /// </summary>
    public IReadOnlyList<SkillRef> SkillRefs { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Chave em <see cref="Metadata"/> que carrega o escopo/domínio do
    /// Worker. Vive em metadata em vez de campo top-level pra evitar
    /// propagação manual nos múltiplos paths que reconstroem
    /// <see cref="AgentDefinition"/>. Lida pelo runtime
    /// (<c>ChatOptionsBuilder</c>) e validação (<c>AgentService</c>).
    /// </summary>
    public const string WorkerScopeMetadataKey = "x-worker-scope";

    /// <summary>
    /// Chave em <see cref="Metadata"/> que declara, pra Tool Runner, que o
    /// agente deve aguardar aprovação humana antes de invocar tools com
    /// side-effect. Valor: <c>"true"</c> ativo / qualquer outro inativo.
    /// Flag puramente declarativo: <c>AgentService.ValidateToolRunner</c>
    /// usa pra emitir warning quando há tools com
    /// <c>RequiresApproval=true</c> e a flag está off. Runtime de chamada
    /// de tool não checa este flag — apenas a validação de save.
    /// </summary>
    public const string ToolRunnerHitlRequiredMetadataKey = "x-tool-runner-hitl-required";

    /// <summary>
    /// Chave em <see cref="Metadata"/> que declara, pra Router, que o
    /// agente roda em workflow <c>InputMode=Chat</c>. Valor: <c>"true"</c>
    /// ativo / qualquer outro inativo. Quando ativo, o codec do wizard
    /// ativa o middleware <c>StructuredOutputState</c> no save — ele lê
    /// o JSON do classify e dispara <c>STATE_DELTA</c> via SSE pra que o
    /// frontend chat consiga reagir em tempo real à intent escolhida.
    /// <c>AgentService.ValidateRouter</c> usa pra emitir warning quando a
    /// flag está on mas o middleware não está presente (ou vice-versa).
    /// </summary>
    public const string RouterForChatMetadataKey = "x-router-for-chat";

    /// <summary>
    /// Chave em <see cref="Metadata"/> que carrega, pra Conversational, a
    /// lista canônica de valores válidos de <c>ui_component</c>. Persiste
    /// como JSON array de strings (ex: <c>["text","card","list","form"]</c>).
    /// O codec de save usa pra injetar o enum no schema fixo
    /// <c>{ui_component, message, output}</c> do StructuredOutput; o
    /// frontend chat consome o enum pra dirigir o renderer (switch ou
    /// fallback genérico). Lista vazia / chave ausente = enum sem
    /// restrição (string livre), com warning soft no save.
    /// </summary>
    public const string ConversationalUiComponentsMetadataKey = "x-conversational-ui-components";

    /// <summary>
    /// Chave em <see cref="Metadata"/> que carrega, pra Conversational, o
    /// texto de persona do agente (papel, personalidade, estilo). Backend
    /// concatena em <c>Instructions</c> no codec do save. Persiste em
    /// metadata em vez de campo top-level pra evitar propagação manual nos
    /// múltiplos paths que reconstroem <see cref="AgentDefinition"/>.
    /// </summary>
    public const string ConversationalPersonaMetadataKey = "x-conversational-persona";

    /// <summary>
    /// "project" (default) — agent visível apenas dentro do projeto dono.
    /// "global" — agent visível a todos os projetos do mesmo tenant; outros projetos
    /// podem referenciá-lo em workflows. Cross-tenant é proibido.
    /// </summary>
    /// <remarks>
    /// Mutável (não init-only) pra permitir hidratação consistente após deserialização —
    /// PgAgentDefinitionRepository.Hydrate sobrescreve com row.Visibility (single source of truth).
    /// </remarks>
    public string Visibility { get; set; } = "project";

    /// <summary>
    /// Tenant denormalizado de projects.tenant_id. Usado pelo HasQueryFilter pra
    /// enforçar tenant boundary em listagens cross-project (Visibility=global).
    /// Populado no upsert via lookup do owner project; default 'default' pra BC.
    /// </summary>
    public string TenantId { get; set; } = "default";

    /// <summary>
    /// Whitelist explícita de projetos que podem usar este agent quando
    /// Visibility=global. Semântica:
    /// <list type="bullet">
    ///   <item><c>null</c> (default) — qualquer projeto do tenant pode referenciar (visibility implícita).</item>
    ///   <item>Lista vazia — bloqueado pra todos exceto o owner.</item>
    ///   <item>Lista com IDs — apenas esses projetos + owner podem referenciar.</item>
    /// </list>
    /// Owner sempre tem acesso (não precisa estar na lista). Sem efeito quando
    /// Visibility=project. Usado pelo AgentFactory antes de criar o chat client
    /// (não em runtime LLM) e pelo WorkflowValidator no save.
    /// </summary>
    public IReadOnlyList<string>? AllowedProjectIds { get; set; }

    /// <summary>
    /// Agent ligado/desligado. Default true. Quando false, workflows que referenciam o
    /// agent continuam saváveis (warning UI), mas o agent é pulado em runtime — pipeline
    /// continua sem invocar o agent. Permite manutenção sem migrar todos os workflows callers.
    /// Mutável via PATCH /api/aihub/agents/{id}/enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public static readonly IReadOnlySet<string> AllowedVisibilities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "project", "global" };

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// TestSet usado pelo autotrigger em publish de AgentVersion. Quando null,
    /// autotrigger é no-op (warning UI sinalizando regression baseline missing,
    /// sem bloquear o publish).
    /// </summary>
    public string? RegressionTestSetId { get; init; }

    /// <summary>
    /// Snapshot de EvaluatorConfig usado pelo autotrigger. Aponta para uma
    /// version específica (não o header) — mudar bindings cria nova revision
    /// sem invalidar histórico de runs.
    /// </summary>
    public string? RegressionEvaluatorConfigVersionId { get; init; }

    /// <summary>
    /// Gate "validated for chat" denormalizado da tabela
    /// <c>aihub.chat_sandbox_sessions</c> — populado pelo ChatSandboxService ao
    /// validar uma session, zerado pelo approval flow quando nova AgentVersion
    /// é publicada. Não viaja no JSON do <c>Data</c> jsonb (vive em colunas
    /// próprias da row; <see cref="JsonIgnoreAttribute"/> evita duplicação).
    /// </summary>
    [JsonIgnore]
    public DateTime? LastChatSandboxValidatedAt { get; set; }

    [JsonIgnore]
    public string? LastChatSandboxValidatedByUserId { get; set; }

    [JsonIgnore]
    public string? LastChatSandboxValidatedAgentVersionId { get; set; }

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
        if (Model is null)
            throw new DomainException("AgentDefinition.Model é obrigatório.");
        // DeploymentName só é obrigatório quando NÃO há preset — runtime binder
        // hidrata DeploymentName/Temperature/MaxTokens/Provider do preset.
        if (string.IsNullOrWhiteSpace(Model.PredefinedModelId)
            && string.IsNullOrWhiteSpace(Model.DeploymentName))
            throw new DomainException(
                "AgentDefinition.Model.DeploymentName é obrigatório quando PredefinedModelId não é informado.");
        if (Model.Temperature is < 0 or > 2)
            throw new DomainException("AgentDefinition.Model.Temperature deve estar em [0, 2] quando presente.");
        if (!AllowedVisibilities.Contains(Visibility))
            throw new DomainException(
                $"AgentDefinition.Visibility inválida: '{Visibility}'. Permitidos: {string.Join(", ", AllowedVisibilities)}.");

        // Whitelist só faz sentido com Visibility=global. Com Visibility=project,
        // qualquer valor não-null é confuso/contraproducente.
        if (AllowedProjectIds is not null
            && !string.Equals(Visibility, "global", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "AgentDefinition.AllowedProjectIds só pode ser definido quando Visibility=global.");
    }

    /// <summary>
    /// Decide se o caller project pode referenciar este agent baseado em
    /// Visibility, ownership e whitelist (AllowedProjectIds).
    /// </summary>
    public bool CanBeReferencedBy(string callerProjectId)
    {
        // Owner sempre tem acesso.
        if (string.Equals(ProjectId, callerProjectId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Não-owner: precisa Visibility=global.
        if (!string.Equals(Visibility, "global", StringComparison.OrdinalIgnoreCase))
            return false;

        // null = sem whitelist explícita; qualquer projeto do tenant pode (boundary tenant
        // é enforced em camadas acima — query filter no DbContext).
        if (AllowedProjectIds is null) return true;

        return AllowedProjectIds.Any(p => string.Equals(p, callerProjectId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Combina <see cref="Enabled"/> + <see cref="CanBeReferencedBy"/> — útil pra
    /// AgentFactory decidir se invoca o agent num workflow do caller.
    /// </summary>
    public bool CanBeInvokedBy(string callerProjectId) =>
        Enabled && CanBeReferencedBy(callerProjectId);
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
    /// <summary>
    /// Nome do deployment do modelo no provider. Default vazio quando o agent
    /// usa <see cref="PredefinedModelId"/> (binder hidrata em runtime). A
    /// invariante <c>EnsureInvariants</c> exige preenchimento aqui OU em
    /// <see cref="PredefinedModelId"/> — não pode haver os dois vazios.
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;
    public float? Temperature { get; init; }
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Quando setado, referencia um preset em <c>aihub.predefined_models</c>.
    /// O <c>PredefinedModelBinder</c> resolve em runtime e substitui
    /// <see cref="DeploymentName"/>/<see cref="Temperature"/>/<see cref="MaxTokens"/>
    /// + <c>Provider</c> pelos valores do preset. Quando presente, invariantes
    /// relaxam — DeploymentName pode estar vazio (será preenchido pelo binder).
    /// </summary>
    public string? PredefinedModelId { get; init; }
}

public class AgentToolDefinition
{
    /// <summary>"code_interpreter" | "file_search" | "function" | "generic_http" | "mcp" | "web_search"</summary>
    public required string Type { get; init; }

    public string? Name { get; init; }
    public bool RequiresApproval { get; init; } = false;

    /// <summary>
    /// Quando <see cref="Type"/>="generic_http", referência ao Id imutável de um
    /// <c>GenericTool</c> cadastrado no projeto. Os campos <see cref="UrlTemplate"/>
    /// e seguintes carregam a definição completa expandida no momento do save —
    /// runtime consome direto sem consultar o repositório de tools.
    /// </summary>
    public string? GenericToolId { get; init; }

    /// <summary>
    /// Fingerprint (sha256 canônico de <c>{Name, Description, JsonSchema}</c>)
    /// da versão da tool esperada no momento do snapshot do agente. Populado em
    /// <c>PgAgentDefinitionRepository.UpsertAsync</c>. O <c>ChatOptionsBuilder</c>
    /// resolve por este hash (fail-fast quando a tool evoluiu, salvo feature flag
    /// <c>AllowToolFingerprintMismatch</c>).
    /// </summary>
    public string? FingerprintHash { get; init; }

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

    /// <summary>Para web_search (Bing Grounding): connectionId do Azure AI Foundry.</summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// Resumo da tool exposto ao LLM como parte do prompt structural. Copiado
    /// de <c>GenericTool.Description</c> quando <see cref="Type"/>="generic_http"
    /// no save; demais tipos preenchem com a documentação inline da entrada.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>HTTP verb da invocação. Inline pra runtime não consultar o repo de tools.</summary>
    public HttpMethodType? HttpMethod { get; init; }

    /// <summary>
    /// Template da URL com placeholders <c>{nome}</c> resolvidos via
    /// <see cref="PathParams"/>. Copiado de <c>GenericTool.UrlTemplate</c>.
    /// </summary>
    public string? UrlTemplate { get; init; }

    /// <summary>Definições dos placeholders do <see cref="UrlTemplate"/>.</summary>
    public IReadOnlyDictionary<string, ParamDefinition>? PathParams { get; init; }

    /// <summary>Definições das query strings opcionais ou obrigatórias.</summary>
    public IReadOnlyDictionary<string, ParamDefinition>? QueryParams { get; init; }

    /// <summary>Headers extras com valores fixos enviados em toda invocação.</summary>
    public IReadOnlyDictionary<string, string>? CustomHeaders { get; init; }

    /// <summary>Content-Type do body. <c>None</c> quando <see cref="HttpMethod"/> é GET/DELETE.</summary>
    public InputContentType? InputContentType { get; init; }

    /// <summary>JSON Schema raw do body. Null quando <see cref="InputContentType"/> é None ou Text.</summary>
    public string? InputSchemaJson { get; init; }

    /// <summary>Content-Type esperado na resposta — usado pelo parser do executor.</summary>
    public OutputContentType? OutputContentType { get; init; }

    /// <summary>JSON Schema raw da resposta. Null quando <see cref="OutputContentType"/> é Text.</summary>
    public string? OutputSchemaJson { get; init; }

    /// <summary>Modo de projeção (drop-extras / strict / off) aplicado contra <see cref="OutputSchemaJson"/>.</summary>
    public OutputProjectionMode? OutputProjectionMode { get; init; }

    /// <summary>Override por-tool do timeout em segundos. Null = usa default global do executor.</summary>
    public int? TimeoutSecondsOverride { get; init; }

    /// <summary>Texto livre que orienta o LLM sobre quando invocar a tool. Vira gatilho no prompt.</summary>
    public string? WhenToUse { get; init; }

    /// <summary>
    /// Quando true, o executor encaminha <c>app_origin</c> + <c>access_token</c>
    /// da request original (autorização via token do usuário). Quando false ou null,
    /// chamada anônima/com headers fixos apenas.
    /// </summary>
    public bool? IsExclusive { get; init; }

    /// <summary>
    /// Quando setado, identifica que esta entrada foi mesclada a partir de uma
    /// <see cref="SkillRef"/> referenciada pelo agente — o composer popula no
    /// momento do save. Tools autorais (criadas direto pelo owner) deixam o
    /// campo null. O decomposer usa esse marker pra ocultar tools provenientes
    /// de skill no GET, devolvendo só o que o cliente declara.
    /// </summary>
    public string? SourceSkillId { get; init; }
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

/// <summary>
/// Configuração da memória operacional do agente. Schema declara a forma
/// canônica do estado que o LLM emite/lê a cada turno; <see cref="MaxBytes"/>
/// limita o tamanho persistido pra evitar inflar o prompt indefinidamente.
/// </summary>
public class AgentOperationalMemoryDefinition
{
    /// <summary>Default usado quando <see cref="MaxBytes"/> é null.</summary>
    public const int DefaultMaxBytes = 8192;

    /// <summary>JSON Schema do payload da memória. Required — sem schema, não há ativação.</summary>
    public JsonDocument? Schema { get; init; }

    /// <summary>Cap em bytes do payload persistido. Acima disso a escrita é rejeitada e a memória anterior preservada.</summary>
    public int? MaxBytes { get; init; }
}
