using System.Diagnostics;
using EfsAiHub.Core.Abstractions.AgUi;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Agents.Exceptions;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Platform.Runtime.Audit;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Guards;
using EfsAiHub.Platform.Runtime.Middlewares;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Resilience;
using EfsAiHub.Platform.Runtime.Tools.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Orquestra a criação de agentes resolvendo o provider LLM correto,
/// construindo as opções de chat e aplicando decorators de middleware/rastreamento de tokens.
/// </summary>
public class AgentFactory : IAgentFactory
{
    /// <summary>
    /// Middlewares declarados pelo agente que devem ser wrappados ANTES de
    /// <see cref="OperationalMemoryChatClient"/> — ou seja, no OnAfter eles
    /// rodam ANTES da memória ser persistida + strippada.
    ///
    /// <para>
    /// Caso de uso: hard validators que mutam o output do LLM (ex.:
    /// <c>RouterDecisionTelemetry</c> reescreve needs_clarification inválido
    /// pra out_of_scope). Sem essa fase, OpMem persistiria o estado ruim no
    /// banco antes do rewrite — DB ficaria divergente do JSON emitido ao
    /// frontend, e o loop guard via <c>clarification_depth</c> seria
    /// inoperante no próximo turno.
    /// </para>
    ///
    /// <para>
    /// Middlewares fora desta lista mantêm comportamento legado: wrappam
    /// APÓS OpMem (mais externo no pipeline), enxergam output já strippado.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> PreMemoryMiddlewareTypes =
        new(StringComparer.OrdinalIgnoreCase) { "RouterDecisionTelemetry" };

    /// <summary>
    /// Exposto <c>internal</c> pra cobertura por unit tests sem precisar
    /// construir uma <see cref="AgentFactory"/> completa (dezenas de
    /// dependências). Decisão de fase é pura função do nome do middleware
    /// type — testar a lista é suficiente.
    /// </summary>
    internal static bool IsPreMemoryPhase(string type) => PreMemoryMiddlewareTypes.Contains(type);

    private readonly IReadOnlyDictionary<string, ILlmClientProvider> _providers;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IFunctionToolRegistry _functionRegistry;
    private readonly IAgentMiddlewareRegistry _middlewareRegistry;
    private readonly ITokenUsageSink _tokenPersistence;
    private readonly IToolInvocationSink _toolPersistence;
    private readonly EfsAiHub.Platform.Runtime.Tools.Generic.IGenericToolExecutor _genericToolExecutor;
    private readonly ILogger<AgentFactory> _logger;
    private readonly ILogger<TrackedAIFunction> _trackedFnLogger;
    private readonly IModelPricingCache? _pricingCache;
    private readonly LlmCircuitBreaker? _circuitBreaker;
    private readonly bool _allowFingerprintMismatch;
    private readonly IAgUiTokenSink? _agUiTokenSink;
    private readonly IProjectRepository? _projectRepo;
    private readonly IAgentVersionRepository? _agentVersionRepo;
    // Persona personalization — opcionais por design: agents que rodam sem
    // persona (Anonymous) ou ambientes que não configuraram o Persona API
    // ficam com null e o factory cai no prompt base puro.
    private readonly IPersonaPromptComposer? _personaComposer;
    private readonly ISystemMessageBuilder _systemMessageBuilder;
    // Blocklist guardrail. Opcionais por design — null em testes unitários ou ambientes
    // que não habilitaram a feature. Em produção ambos vêm do DI (Singleton).
    private readonly BlocklistEngine? _blocklistEngine;
    private readonly IWorkflowEventBus? _eventBus;
    private readonly EfsAiHub.Core.Abstractions.Observability.IAdminAuditLogger? _auditLogger;
    private readonly EfsAiHub.Core.Abstractions.Identity.IProjectContextAccessor? _projectContextAccessor;
    // Feature flags com IOptionsMonitor (atualização runtime sem restart).
    // Optional pra preservar BC com testes que não injetam.
    private readonly IOptionsMonitor<EfsAiHub.Core.Abstractions.Sharing.SharingOptions>? _sharingOptions;
    // Persistência da memória operacional. Optional: agentes sem
    // OperationalMemory.Schema NÃO acessam o repo, então testes que não
    // exercitam essa feature podem omitir.
    private readonly EfsAiHub.Core.Agents.IOperationalMemoryRepository? _operationalMemoryRepo;

    // LLM Prompt Inspector — captura runtime-toggleable. Os 3 são Optional
    // por design pra preservar BC com testes que não envolvem captura.
    // Quando capture config está OFF (default), middleware bypassa e overhead
    // é ~1 consulta Redis no caminho do request.
    private readonly EfsAiHub.Platform.Runtime.Services.LlmCaptureConfigService? _captureConfig;
    private readonly EfsAiHub.Platform.Runtime.Sanitization.ILlmPayloadSanitizer? _payloadSanitizer;
    private readonly EfsAiHub.Core.Orchestration.Interfaces.ILlmInvocationLogSink? _captureSink;

    // Router Quick Actions — bypass do LLM em mensagens que batem em padrões
    // pré-cadastrados. Optional pra preservar BC com testes que não usam.
    // Quando null, Router sempre invoca LLM (comportamento legado).
    private readonly EfsAiHub.Core.Agents.RouterQuickActions.IRouterQuickActionMatcher? _quickActionMatcher;
    private readonly EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor? _tenantContextAccessor;

    // Throttle pra cross_project_invoke audit. Capacity 1000, janela 60s,
    // emite métrica ao despejar. Static singleton: factory é registrado scoped em DI
    // mas o throttle precisa ser process-wide pra evitar duplicar logs entre scopes.
    private static readonly AuditThrottle _crossProjectAuditThrottle = new(
        window: TimeSpan.FromSeconds(60),
        maxEntries: 1000,
        onEviction: () => EfsAiHub.Infra.Observability.MetricsRegistry.AuditThrottleLruEvictions.Add(1));

    public AgentFactory(
        IEnumerable<ILlmClientProvider> providers,
        IAgentDefinitionRepository agentRepo,
        IFunctionToolRegistry functionRegistry,
        IAgentMiddlewareRegistry middlewareRegistry,
        ITokenUsageSink tokenPersistence,
        IToolInvocationSink toolPersistence,
        EfsAiHub.Platform.Runtime.Tools.Generic.IGenericToolExecutor genericToolExecutor,
        ILogger<AgentFactory> logger,
        ILogger<TrackedAIFunction> trackedFnLogger,
        IModelPricingCache? pricingCache = null,
        IOptions<WorkflowEngineOptions>? engineOptions = null,
        LlmCircuitBreaker? circuitBreaker = null,
        IAgUiTokenSink? agUiTokenSink = null,
        IProjectRepository? projectRepo = null,
        IAgentVersionRepository? agentVersionRepo = null,
        IPersonaPromptComposer? personaComposer = null,
        ISystemMessageBuilder? systemMessageBuilder = null,
        BlocklistEngine? blocklistEngine = null,
        IWorkflowEventBus? eventBus = null,
        EfsAiHub.Core.Abstractions.Observability.IAdminAuditLogger? auditLogger = null,
        EfsAiHub.Core.Abstractions.Identity.IProjectContextAccessor? projectContextAccessor = null,
        IOptionsMonitor<EfsAiHub.Core.Abstractions.Sharing.SharingOptions>? sharingOptions = null,
        EfsAiHub.Core.Agents.IOperationalMemoryRepository? operationalMemoryRepo = null,
        EfsAiHub.Platform.Runtime.Services.LlmCaptureConfigService? captureConfig = null,
        EfsAiHub.Platform.Runtime.Sanitization.ILlmPayloadSanitizer? payloadSanitizer = null,
        EfsAiHub.Core.Orchestration.Interfaces.ILlmInvocationLogSink? captureSink = null,
        EfsAiHub.Core.Agents.RouterQuickActions.IRouterQuickActionMatcher? quickActionMatcher = null,
        EfsAiHub.Core.Abstractions.Identity.ITenantContextAccessor? tenantContextAccessor = null)
    {
        _providers = providers.ToDictionary(p => p.ProviderType, StringComparer.OrdinalIgnoreCase);
        _agentRepo = agentRepo;
        _functionRegistry = functionRegistry;
        _middlewareRegistry = middlewareRegistry;
        _tokenPersistence = tokenPersistence;
        _toolPersistence = toolPersistence;
        _genericToolExecutor = genericToolExecutor;
        _logger = logger;
        _trackedFnLogger = trackedFnLogger;
        _pricingCache = pricingCache;
        _circuitBreaker = circuitBreaker;
        _allowFingerprintMismatch = engineOptions?.Value.AllowToolFingerprintMismatch ?? true;
        _agUiTokenSink = agUiTokenSink;
        _projectRepo = projectRepo;
        _agentVersionRepo = agentVersionRepo;
        _personaComposer = personaComposer;
        _systemMessageBuilder = systemMessageBuilder ?? new SystemMessageBuilder();
        _blocklistEngine = blocklistEngine;
        _eventBus = eventBus;
        _auditLogger = auditLogger;
        _projectContextAccessor = projectContextAccessor;
        _sharingOptions = sharingOptions;
        _operationalMemoryRepo = operationalMemoryRepo;
        _captureConfig = captureConfig;
        _payloadSanitizer = payloadSanitizer;
        _captureSink = captureSink;
        _quickActionMatcher = quickActionMatcher;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<ExecutableWorkflow> CreateAgentAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool isStandaloneFlow = false,
        string? resolvedVersionId = null)
    {
        _logger.LogInformation(
            "Creating agent '{AgentName}' (id: {AgentId}, provider: {Provider}/{ClientType}, standalone: {Standalone})",
            definition.Name, definition.Id,
            definition.Provider.Type, definition.Provider.ClientType, isStandaloneFlow);

        DelegateExecutor.CurrentLogger.Value = _logger;

        definition = await InjectProjectCredentials(definition, ct);
        await TrackAgentVersionAsync(definition.Id, resolvedVersionId, ct);
        TrackPromptVersion(definition);
        var provider = ResolveProvider(definition);
        var options = ChatOptionsBuilder.BuildAgentOptions(
            definition, _functionRegistry, _toolPersistence.Writer, _trackedFnLogger, _logger,
            _genericToolExecutor,
            _allowFingerprintMismatch,
            projectId: definition.ProjectId);

        if (CanWrapAsChatClient(provider, definition))
        {
            var rawClient = await provider.CreateChatClientAsync(definition, ct);
            var wrappedClient = await WrapWithTokenTrackingAsync(rawClient, definition, ct, isStandaloneFlow);
            return ExecutableWorkflow.FromAgent(wrappedClient.AsAIAgent(options));
        }

        return ExecutableWorkflow.FromAgent(await provider.CreateAgentAsync(definition, options, ct));
    }

    /// <summary>
    /// Registra qual PromptVersionId está sendo executado no contexto do
    /// DelegateExecutor pra audit. Snapshot já carrega o id em
    /// <see cref="AgentDefinition.PromptVersionId"/> via <c>ToDefinition</c>.
    /// </summary>
    private static void TrackPromptVersion(AgentDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.PromptVersionId)) return;
        DelegateExecutor.Current.Value?.PromptVersions.TryAdd(definition.Id, definition.PromptVersionId);
    }

    /// <summary>
    /// Providers que expõem um <see cref="IChatClient"/> stateless local-side
    /// passam pelo pipeline de middleware (TokenTracking, Blocklist, OperationalMemory,
    /// etc.). AzureFoundry com <c>ClientType=PersistentAgents</c> mantém state
    /// server-side e não suporta esse pipeline — segue o caminho antigo via
    /// <see cref="ILlmClientProvider.CreateAgentAsync"/>.
    /// </summary>
    private static bool CanWrapAsChatClient(ILlmClientProvider provider, AgentDefinition definition)
    {
        if (provider.ProviderType is "AZUREOPENAI" or "OPENAI")
            return true;
        if (string.Equals(provider.ProviderType, "AZUREFOUNDRY", StringComparison.OrdinalIgnoreCase)
            && string.Equals(definition.Provider.ClientType, "Responses", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>Cria <see cref="IChatClient"/> bare com pipeline completo (sem wrapper de workflow) — usado pelo subsistema de avaliação.</summary>
    public async Task<IChatClient> CreateBareAgentAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating bare agent '{AgentName}' (id: {AgentId}) for evaluation",
            definition.Name, definition.Id);

        DelegateExecutor.CurrentLogger.Value = _logger;

        definition = await InjectProjectCredentials(definition, ct);
        await TrackAgentVersionAsync(definition.Id, null, ct);
        TrackPromptVersion(definition);
        var provider = ResolveProvider(definition);
        var rawClient = await provider.CreateChatClientAsync(definition, ct);
        return await WrapWithTokenTrackingAsync(rawClient, definition, ct);
    }

    public async Task<IReadOnlyDictionary<string, ExecutableWorkflow>> CreateAgentsForWorkflowAsync(
        WorkflowDefinition workflow, CancellationToken ct = default)
    {
        var result = new Dictionary<string, ExecutableWorkflow>();

        // Workflow declarado standalone (default em /workflows criados sem
        // ChatTurnContext) não preserva contexto entre chamadas. Propaga o
        // sinal pros agentes pra que (a) o ResponseFormat não inclua
        // operationalMemory e (b) WrapWithOperationalMemory faça bypass.
        var isStandaloneFlow = string.Equals(
            workflow.Configuration.InputMode, "Standalone",
            StringComparison.OrdinalIgnoreCase);

        foreach (var agentRef in workflow.Agents)
        {
            // Resolve governança (row viva) + comportamento (snapshot da versão pinada)
            // num único ponto — agora compartilhado com o caminho Graph
            // (CreateLlmHandlerAsync), que antes ignorava o pin e rodava current.
            var (definition, resolvedVersionId) = await ResolveAgentDefinitionAsync(
                agentRef.AgentId, agentRef.AgentVersionId, workflow.Id, ct);

            // Agent desligado pelo owner: pula completamente — não entra no dict, não cria
            // chat client, não invoca HITL. Workflow continua execução com agent ausente
            // (Sequential pula step, Graph ignora edges órfãs, GroupChat exclui participant).
            if (!definition.Enabled)
            {
                _logger.LogWarning(
                    "[AgentFactory] Agent '{AgentId}' desabilitado — pulado em workflow '{WorkflowId}'.",
                    definition.Id, workflow.Id);
                EfsAiHub.Infra.Observability.MetricsRegistry.AgentDisabledInvocations.Add(1,
                    new KeyValuePair<string, object?>("agent_id", definition.Id),
                    new KeyValuePair<string, object?>("workflow_id", workflow.Id));
                continue;
            }

            var sharing = _sharingOptions?.CurrentValue;
            var crossProjectEnabled = sharing?.CrossProjectEnabled ?? true;
            var whitelistEnabled = sharing?.WhitelistEnabled ?? true;
            var auditCrossInvokeEnabled = sharing?.AuditCrossInvoke ?? true;

            var isCrossProject = !string.Equals(workflow.ProjectId, definition.ProjectId, StringComparison.OrdinalIgnoreCase);

            // Feature flag CrossProjectEnabled: rollback graceful sem deploy.
            // Quando false, bloqueia toda resolução cross-project.
            if (isCrossProject && !crossProjectEnabled)
            {
                throw new UnauthorizedAccessException(
                    "Cross-project agent resolution está desabilitada (Sharing:CrossProjectEnabled=false).");
            }

            // Whitelist enforcement: bloqueia ANTES de criar chat client
            // (não em runtime LLM, evita custo parcial). Pode ser desligado via flag.
            if (whitelistEnabled && !definition.CanBeReferencedBy(workflow.ProjectId))
            {
                EfsAiHub.Infra.Observability.MetricsRegistry.AgentWhitelistBlocked.Add(1,
                    new KeyValuePair<string, object?>("caller_project", workflow.ProjectId),
                    new KeyValuePair<string, object?>("owner_project", definition.ProjectId),
                    new KeyValuePair<string, object?>("agent_id", definition.Id));

                throw new UnauthorizedAccessException(
                    $"Agent '{definition.Id}' não está autorizado para o projeto '{workflow.ProjectId}' (whitelist em vigor).");
            }

            // Agent cross-project: caller workflow.ProjectId != agent.ProjectId.
            // Ocorre quando workflow referencia agent global de outro projeto do mesmo tenant.
            // Emite log estruturado + métrica + audit pra rastreabilidade — não bloqueia.
            if (isCrossProject)
            {
                _logger.LogInformation(
                    "[AgentFactory] Cross-project agent resolved. Workflow={WorkflowId} CallerProject={CallerProject} AgentId={AgentId} OwnerProject={OwnerProject} Visibility={Visibility}",
                    workflow.Id, workflow.ProjectId, definition.Id, definition.ProjectId, definition.Visibility);

                EfsAiHub.Infra.Observability.MetricsRegistry.AgentCrossProjectInvocations.Add(1,
                    new KeyValuePair<string, object?>("caller_project", workflow.ProjectId),
                    new KeyValuePair<string, object?>("owner_project", definition.ProjectId),
                    new KeyValuePair<string, object?>("tenant", definition.TenantId));

                // Throttle: log no máximo 1× por (caller, owner, agent) a cada 60s
                // pra evitar inflar audit em workloads alto (workflow loops). Métrica
                // agents.cross_project_invocations_total (sem throttle) cobre toda chamada;
                // audit row é o "evento de governança" amostrado. Pode ser desligado via flag
                // Sharing:AuditCrossInvoke=false em ambientes com pressão alta na audit table.
                var throttleKey = $"{workflow.ProjectId}|{definition.ProjectId}|{definition.Id}";
                var shouldAudit = auditCrossInvokeEnabled && _crossProjectAuditThrottle.ShouldLog(throttleKey);

                if (_auditLogger is not null && shouldAudit)
                {
                    try
                    {
                        var payload = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            callerProjectId = workflow.ProjectId,
                            ownerProjectId = definition.ProjectId,
                            workflowId = workflow.Id,
                            agentId = definition.Id,
                        }, JsonDefaults.Domain));
                        await _auditLogger.RecordAsync(new EfsAiHub.Core.Abstractions.Observability.AdminAuditEntry
                        {
                            ActorUserId = "system:agent-factory",
                            ActorUserType = "system",
                            Action = EfsAiHub.Core.Abstractions.Observability.AdminAuditActions.CrossProjectInvoke,
                            ResourceType = EfsAiHub.Core.Abstractions.Observability.AdminAuditResources.Agent,
                            ResourceId = definition.Id,
                            ProjectId = workflow.ProjectId,
                            TenantId = definition.TenantId,
                            PayloadAfter = payload,
                            Timestamp = DateTime.UtcNow,
                        }, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[AgentFactory] Falha ao registrar audit cross_project_invoke (não-bloqueante).");
                    }
                }
            }

            result[agentRef.AgentId] = await CreateAgentAsync(definition, ct, isStandaloneFlow, resolvedVersionId);
        }

        return result;
    }

    /// <summary>
    /// Resolve a AgentDefinition efetiva de um agentRef de workflow: governança
    /// (Visibility/ProjectId/Enabled/...) vem da row viva; comportamento
    /// (instructions/schema/tools/model) vem do snapshot da versão pinada via
    /// <see cref="IAgentVersionRepository.ResolveEffectiveAsync"/> (patch-propagation:
    /// current se não-breaking entre pin e current, pin exato se breaking). Retorna
    /// também o AgentVersionId EFETIVO (o que de fato roda) pra telemetria. Sem pin /
    /// sem repo → row viva (current). Compartilhado entre Graph e não-Graph.
    /// </summary>
    private async Task<(AgentDefinition Definition, string? ResolvedVersionId)> ResolveAgentDefinitionAsync(
        string agentId, string? agentVersionId, string workflowIdForLog, CancellationToken ct)
    {
        // Governance source = live row (Visibility/ProjectId/TenantId/Enabled são
        // mutáveis e cross-cutting; mudança no owner afeta workflows pinados).
        var governanceSource = await _agentRepo.GetByIdAsync(agentId, ct);
        if (governanceSource is null)
        {
            // Orphan: pin existente sem agent_definitions row é caso operacional crítico
            // (deleted owner, drift). Métrica antes do throw pra ops capturarem.
            if (!string.IsNullOrEmpty(agentVersionId))
                EfsAiHub.Infra.Observability.MetricsRegistry.AgentVersionGovernanceMissing.Add(1,
                    new KeyValuePair<string, object?>("agent_id", agentId));
            throw new InvalidOperationException(
                $"Agent '{agentId}' referenced in workflow '{workflowIdForLog}' not found.");
        }

        if (!string.IsNullOrEmpty(agentVersionId) && _agentVersionRepo is not null)
        {
            // Pin setado: reconstrói do snapshot lossless hidratando governança da row viva.
            var snapshot = await _agentVersionRepo.ResolveEffectiveAsync(agentId, agentVersionId, ct);
            var definition = snapshot.ToDefinition(governanceSource);
            // Enabled é governança mutável e ToDefinition não hidrata — puxa da row viva
            // pra que o skip de agente desligado funcione em workflow pinado.
            definition.Enabled = governanceSource.Enabled;

            var strategy = string.Equals(snapshot.AgentVersionId, agentVersionId, StringComparison.OrdinalIgnoreCase)
                ? "exact" : "propagated";
            EfsAiHub.Infra.Observability.MetricsRegistry.AgentVersionPinResolutions.Add(1,
                new KeyValuePair<string, object?>("strategy", strategy),
                new KeyValuePair<string, object?>("agent_id", agentId));
            return (definition, snapshot.AgentVersionId);
        }

        // Sem pin / sem repo: row viva (current). Validator exige pin no save, então
        // pin vazio com repo presente sinaliza divergência — emite métrica.
        if (!string.IsNullOrEmpty(agentVersionId) || _agentVersionRepo is null)
            EfsAiHub.Infra.Observability.MetricsRegistry.AgentVersionPinResolutions.Add(1,
                new KeyValuePair<string, object?>("strategy", "no_pin_unexpected"),
                new KeyValuePair<string, object?>("agent_id", agentId));
        return (governanceSource, null);
    }

    public async Task<Func<string, CancellationToken, Task<string>>> CreateLlmHandlerAsync(
        string agentId, string? agentVersionId = null, CancellationToken ct = default, bool isStandaloneFlow = false)
    {
        // Honra o pin de versão do agente (agentRef.AgentVersionId vindo do snapshot do
        // workflow). Antes esse caminho Graph rodava sempre a row current, ignorando o
        // pin — mesma resolução do caminho não-Graph (CreateAgentsForWorkflowAsync).
        var (definition, resolvedVersionId) = await ResolveAgentDefinitionAsync(
            agentId, agentVersionId, "graph_handler", ct);

        // Agent desligado: lança AgentDisabledException pra caller (BuildBindingMapAsync no
        // Graph mode) skipar a chave do bindingMap. Pipeline continua sem o agent.
        if (!definition.Enabled)
        {
            _logger.LogWarning(
                "[AgentFactory] Agent '{AgentId}' desabilitado — handler não criado.",
                definition.Id);
            EfsAiHub.Infra.Observability.MetricsRegistry.AgentDisabledInvocations.Add(1,
                new KeyValuePair<string, object?>("agent_id", definition.Id),
                new KeyValuePair<string, object?>("workflow_id", "graph_handler"));
            throw new AgentDisabledException(definition.Id);
        }

        definition = await InjectProjectCredentials(definition, ct);
        // Telemetria com a versão EFETIVA resolvida (não current) — coerente com o
        // comportamento que de fato roda quando há pin.
        var trackedVersionId = await TrackAgentVersionAsync(agentId, resolvedVersionId, ct);
        TrackPromptVersion(definition);
        var provider = ResolveProvider(definition);
        var rawChatClient = await provider.CreateChatClientAsync(definition, ct);
        var chatOptions = ChatOptionsBuilder.BuildGraphChatOptions(
            definition, _functionRegistry, _toolPersistence.Writer, _trackedFnLogger, _logger,
            _genericToolExecutor,
            _allowFingerprintMismatch, projectId: definition.ProjectId);

        // Envolve com FunctionInvokingChatClient para tratar chamadas de ferramentas automaticamente no modo Graph.
        // Sem isso, o handler lê apenas response.Text, que fica vazio quando o modelo retorna uma tool call.
        // O wrapper faz o loop: LLM→ferramenta→LLM até o modelo produzir uma resposta de texto final.
        IChatClient chatClient = chatOptions.Tools is { Count: > 0 }
            ? new FunctionInvokingChatClient(rawChatClient) { MaximumIterationsPerRequest = 10 }
            : rawChatClient;

        // LlmInvocationCapture ANTES de WrapWithMiddlewares — assim fica INTERNO à memória
        // operacional e demais middlewares, gravando o request EXATAMENTE como chega ao modelo
        // (com o bloco <operational_memory>, RAG, etc.) e a resposta crua. Externo ao
        // FunctionInvoking → uma row por turno (não por iteração do tool loop). Opt-in via DI;
        // com captura OFF (default), curto-circuita pra inner sem alocação.
        // P1-8: reusa agentVersionId já resolvido na linha 415 — evita lookup duplicado no DB.
        if (_captureConfig is not null && _payloadSanitizer is not null && _captureSink is not null)
        {
            var captureModelId = definition.Model.DeploymentName ?? "unknown";
            chatClient = new EfsAiHub.Platform.Runtime.Middlewares.LlmInvocationCaptureChatClient(
                chatClient,
                agentId: definition.Id,
                agentVersionId: trackedVersionId,
                modelId: captureModelId,
                provider: definition.Provider.Type,
                configService: _captureConfig,
                sanitizer: _payloadSanitizer,
                writer: _captureSink.Writer,
                logger: _logger);
        }

        chatClient = WrapWithMiddlewares(chatClient, definition, isStandaloneFlow);

        // Snapshot já trouxe Instructions composto e PromptVersionId — runtime
        // não toca o repo de prompts.
        var instructions = definition.Instructions;
        var promptVersionId = definition.PromptVersionId;

        var modelId = definition.Model.DeploymentName ?? "unknown";
        var usageWriter = _tokenPersistence.Writer;
        var logger = _logger;

        return async (input, cancellationToken) =>
        {
            // Pré-ativa o PromptComposition AsyncLocal pra que contribuidores
            // que rodam ANTES do middleware capture (SystemMessageBuilder,
            // ChatOptionsBuilder, AgentFactory history/input track) consigam
            // anotar suas seções. O middleware capture detecta presença prévia
            // via flag `ownsComposition` e preserva — não sobrescreve. Quando
            // captura está OFF, ambient continua null (zero overhead).
            var compositionAlreadySet = EfsAiHub.Core.Agents.Composition.PromptCompositionAmbient.Current is not null;
            var captureActive = !compositionAlreadySet
                && _captureConfig is not null
                && await IsCaptureLiveAsync(definition, cancellationToken);
            if (captureActive)
                EfsAiHub.Core.Agents.Composition.PromptCompositionAmbient.Current = new EfsAiHub.Core.Agents.Composition.PromptComposition();

            try
            {
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>();

            // Persona: resolvida lazy a partir do ExecutionContext corrente
            // (AsyncLocal). Anonymous ou null → composer retorna Empty e o system
            // message cai para o prompt base invariante sem custo adicional.
            // Mantendo a ordem instructions → persona preserva prefixo cacheável
            // do OpenAI (docs oficiais: prompt caching exige prefix exato estável).
            var execCtx = EfsAiHub.Core.Orchestration.Executors.DelegateExecutor.Current.Value;
            var persona = execCtx?.Persona;
            var projectId = execCtx?.ProjectId;
            // Cadeia de 5 níveis no composer: project:{pid}:agent:{aid}:{userType}
            // → project:{pid}:{userType} → agent:{aid}:{userType} → global:{userType} → null.
            var composedPersona = _personaComposer is null
                ? ComposedPersonaPrompt.Empty
                : await _personaComposer.ComposeAsync(persona, agentId, projectId, cancellationToken);

            var systemMessage = _systemMessageBuilder.Build(instructions ?? string.Empty, composedPersona);
            if (!string.IsNullOrWhiteSpace(systemMessage))
                messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, systemMessage));

            // Tentar expandir ChatTurnContext em mensagens separadas (Graph+Chat mode).
            // Se o input for ChatTurnContext JSON, expande history + metadata + mensagem atual.
            // Senão, usa o input como mensagem User única (comportamento original).
            // O reforço de persona (≤15 tokens) é anexado à última user message pelo
            // ChatTurnContextMapper quando há expansão; em inputs crus o factory append aqui.
            // historyWindow per-agente: Router=5 (canônico), demais herdam o
            // global do workflow (=null no mapper, sem slice). Override
            // declarativo via WorkflowAgentReference fica como follow-up —
            // hoje o factory não tem o workflowRef em escopo.
            var historyWindow = EfsAiHub.Platform.Runtime.Application.Services
                .WorkflowAgentHistoryResolver.Resolve(definition.Type, workflowRef: null, workflowConfig: null);
            // Router não recebe sharedState: sinal de continuação já vem dos
            // markers [ASSISTANT-*] no histórico + operational_memory próprio.
            // sharedState carrega detalhes operacionais de Conversational que
            // viram noise pro classificador (e tokens a mais no TTFT).
            var includeSharedState = definition.Type != AgentType.Router;
            var expanded = ChatTurnContextMapper.TryExpand(
                input,
                composedPersona.UserReinforcement,
                historyWindow: historyWindow,
                includeSharedState: includeSharedState,
                // Mapa completo id→nome do workflow (via ExecutionContext) pra renderizar
                // drafts de OUTROS agentes no shared state com nome; fallback pro próprio.
                agentNamesById: execCtx?.AgentNamesById
                    ?? new Dictionary<string, string> { [definition.Id] = definition.Name });

            // Anota provenance per-message: cada item adicionado é tracable
            // pela posição no array final de `messages`.
            var expansionStart = messages.Count;
            if (expanded is not null)
                messages.AddRange(expanded);
            else
            {
                var userText = composedPersona.UserReinforcement is null
                    ? input
                    : $"{input}\n\n{composedPersona.UserReinforcement}";
                messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userText));
            }

            TrackHistoryAndInputProvenance(
                messages,
                fromIndex: expansionStart,
                hasReinforcement: !string.IsNullOrEmpty(composedPersona.UserReinforcement),
                historyWindow: historyWindow);

            // ── Router Quick Action bypass ──────────────────────────────────────
            // Pra agentes Router, antes de chamar LLM, tenta casar a última user
            // message contra atalhos pré-cadastrados. Hit → output sintético
            // idêntico ao que LLM produziria, zero llm_token_usage row, latência
            // sub-ms. Miss → fluxo LLM normal segue abaixo.
            if (definition.Type == EfsAiHub.Core.Agents.AgentType.Router
                && _quickActionMatcher is not null
                && _tenantContextAccessor is not null)
            {
                var lastUserText = ExtractLastUserText(messages);
                if (!string.IsNullOrWhiteSpace(lastUserText))
                {
                    var tenantId = _tenantContextAccessor.Current.TenantId;
                    var routerProjectId = definition.ProjectId;
                    var match = await _quickActionMatcher.TryMatchAsync(
                        definition.Id, tenantId, routerProjectId, lastUserText!, cancellationToken);
                    if (match is not null)
                    {
                        EfsAiHub.Infra.Observability.MetricsRegistry.RouterQuickActionHits.Add(1,
                            new KeyValuePair<string, object?>("agent_id", definition.Id),
                            new KeyValuePair<string, object?>("intent", match.Intent));
                        _logger.LogInformation(
                            "[QuickAction] Router '{AgentId}' bypass — pattern='{Pattern}' intent='{Intent}'.",
                            definition.Id, match.Pattern, match.Intent);
                        return EfsAiHub.Core.Agents.RouterQuickActions
                            .RouterSyntheticOutputBuilder.Build(match.Intent, match.Pattern);
                    }
                }
            }

            var sw = Stopwatch.StartNew();
            var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
            sw.Stop();

            var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
            var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
            var totalTokens = (int)(response.Usage?.TotalTokenCount ?? inputTokens + outputTokens);

            if (totalTokens > 0)
            {
                MetricsRegistry.AgentTokensUsed.Record(totalTokens,
                    new KeyValuePair<string, object?>("agent_id", agentId),
                    new KeyValuePair<string, object?>("model_id", modelId));
            }

            logger.LogInformation(
                "[TokenUsage] Agent={AgentId} Model={ModelId} Input={InputTokens} Output={OutputTokens} Total={TotalTokens} Duration={DurationMs:F0}ms",
                agentId, modelId, inputTokens, outputTokens, totalTokens, sw.Elapsed.TotalMilliseconds);

            usageWriter.TryWrite(new LlmTokenUsage
            {
                AgentId = agentId,
                ModelId = modelId,
                ExecutionId = DelegateExecutor.Current.Value?.ExecutionId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = totalTokens,
                DurationMs = sw.Elapsed.TotalMilliseconds,
                PromptVersionId = promptVersionId,
                AgentVersionId = agentVersionId,
                CreatedAt = DateTime.UtcNow
            });

            return response.Text ?? string.Empty;
            }
            finally
            {
                if (captureActive)
                    EfsAiHub.Core.Agents.Composition.PromptCompositionAmbient.Current = null;
            }
        };
    }

    /// <summary>
    /// Consulta o <see cref="LlmCaptureConfigService"/> pra decidir se vale
    /// a pena ativar o <c>PromptComposition</c> ambient pré-handler. Falha
    /// silenciosa retorna false — capture é debug, perder uma turn é OK.
    /// </summary>
    private async Task<bool> IsCaptureLiveAsync(AgentDefinition definition, CancellationToken ct)
    {
        if (_captureConfig is null) return false;
        try
        {
            var cfg = await _captureConfig.GetCurrentAsync(ct).ConfigureAwait(false);
            var execCtx = EfsAiHub.Core.Orchestration.Executors.DelegateExecutor.Current.Value;
            return cfg.Matches(
                projectId: execCtx?.ProjectId ?? definition.ProjectId,
                agentId: definition.Id,
                workflowId: execCtx?.WorkflowId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Anota provenance per-message pra <c>history.*</c>, <c>input.user</c> e
    /// reforço de persona. Chamado após o <c>ChatTurnContextMapper.TryExpand</c>
    /// ou o append direto do input cru. No-op quando captura está OFF
    /// (PromptCompositionAmbient.Current=null).
    /// </summary>
    private static void TrackHistoryAndInputProvenance(
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> messages,
        int fromIndex,
        bool hasReinforcement,
        int? historyWindow)
    {
        if (EfsAiHub.Core.Agents.Composition.PromptCompositionAmbient.Current is null) return;

        // Última user message = input atual; user/assistant anteriores = history.
        // System messages dentro deste range (raros — só vêm do mapper expanding
        // metadata da sessão) marcamos como "context.metadata".
        int lastUserIndex = -1;
        for (int i = messages.Count - 1; i >= fromIndex; i--)
        {
            if (messages[i].Role == Microsoft.Extensions.AI.ChatRole.User)
            {
                lastUserIndex = i;
                break;
            }
        }

        for (int i = fromIndex; i < messages.Count; i++)
        {
            var m = messages[i];
            string source;
            string? note = null;
            if (m.Role == Microsoft.Extensions.AI.ChatRole.System)
            {
                source = "context.metadata";
                note = "mapper expansion";
            }
            else if (i == lastUserIndex)
            {
                source = "input.user";
                if (hasReinforcement) note = "with persona reinforcement";
            }
            else
            {
                source = m.Role == Microsoft.Extensions.AI.ChatRole.Assistant
                    ? "history.assistant"
                    : "history.user";
                if (historyWindow is { } w) note = $"window={w}";
            }
            EfsAiHub.Core.Agents.Composition.PromptCompositionAmbient.Track(
                source: source,
                contributorType: nameof(AgentFactory),
                messageIndex: i,
                note: note);
        }
    }

    private ILlmClientProvider ResolveProvider(AgentDefinition definition)
    {
        var type = definition.Provider.Type;
        if (_providers.TryGetValue(type, out var provider))
            return provider;

        if (_providers.TryGetValue("AZUREFOUNDRY", out var foundry))
            return foundry;

        throw new NotSupportedException($"Provider '{type}' not supported and no AzureFoundry fallback registered.");
    }

    /// <summary>
    /// Registra o AgentVersionId para LlmTokenUsage/captura. Prefere a versão EFETIVA
    /// já resolvida (<paramref name="resolvedVersionId"/>) — a que de fato roda quando
    /// há pin. Sem ela (eval/standalone), faz fallback pra current (comportamento legado).
    /// </summary>
    private async Task<string?> TrackAgentVersionAsync(string agentId, string? resolvedVersionId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(resolvedVersionId))
        {
            DelegateExecutor.Current.Value?.AgentVersions?.TryAdd(agentId, resolvedVersionId);
            return resolvedVersionId;
        }

        if (_agentVersionRepo is null) return null;

        try
        {
            var current = await _agentVersionRepo.GetCurrentAsync(agentId, ct);
            if (current is null) return null;

            DelegateExecutor.Current.Value?.AgentVersions?.TryAdd(agentId, current.AgentVersionId);
            _logger.LogDebug("[AgentFactory] Agent '{AgentId}': tracking version '{VersionId}' (rev {Revision}).",
                agentId, current.AgentVersionId, current.Revision);
            return current.AgentVersionId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AgentFactory] Failed to resolve agent version for '{AgentId}' — continuing without tracking.", agentId);
            return null;
        }
    }

    private async Task<IChatClient> WrapWithTokenTrackingAsync(
        IChatClient inner, AgentDefinition definition, CancellationToken ct,
        bool isStandaloneFlow = false)
    {
        var modelId = definition.Model.DeploymentName ?? "unknown";

        IChatClient current = inner;

        // LlmInvocationCapture é o wrapper MAIS INTERNO (colado no provider): grava o request
        // EXATAMENTE como chega ao modelo — já com o bloco <operational_memory>, RAG e demais
        // injeções dos middlewares internos — e a resposta crua do modelo. RetryingChatClient
        // (externo) reentra a cadeia, então cada tentativa vira uma row distinta com o mesmo
        // TurnId. Opt-in: sem os 3 deps injetados, não envolve (mantém BC com testes).
        // Trade-off: chamadas curto-circuitadas/fallback do CircuitBreaker (provider bare, fora
        // desta cadeia) não passam por aqui e não são capturadas — cenário raro de degradação.
        if (_captureConfig is not null && _payloadSanitizer is not null && _captureSink is not null)
        {
            var versionId = EfsAiHub.Core.Orchestration.Executors.DelegateExecutor.Current.Value?
                .AgentVersions?.TryGetValue(definition.Id, out var v) == true ? v : null;
            current = new EfsAiHub.Platform.Runtime.Middlewares.LlmInvocationCaptureChatClient(
                current,
                agentId: definition.Id,
                agentVersionId: versionId,
                modelId: modelId,
                provider: definition.Provider.Type,
                configService: _captureConfig,
                sanitizer: _payloadSanitizer,
                writer: _captureSink.Writer,
                logger: _logger);
        }

        // Cadeia: Retry → Circuit → Blocklist → [POST-MEMORY] → OperationalMemory → [PRE-MEMORY]
        //         → TokenTracking → Capture → Raw
        // agentMaxCostUsd: quando setado em AgentDefinition.CostBudget.MaxCostUsd, o
        // TokenTrackingChatClient emite LogCritical (warning-only) quando o custo
        // acumulado da execução cruza esse teto. Não bloqueia.
        // agentOwnerProjectId: propaga pro audit dual em llm_token_usage. Quando
        // o caller != owner, OriginAgentProjectId é populado; senão null (preserva BC).
        current = new TokenTrackingChatClient(
            current, definition.Id, modelId, _tokenPersistence.Writer, _logger, _pricingCache, _agUiTokenSink,
            agentMaxCostUsd: definition.CostBudget?.MaxCostUsd,
            agentOwnerProjectId: definition.ProjectId);

        // PRE-MEMORY phase: middlewares que precisam rodar no OnAfter ANTES da
        // memória operacional persistir + strippar o payload. Hard validators
        // que mutam output (ex: RouterDecisionTelemetry reescrevendo
        // needs_clarification inválido) entram aqui pra que o estado persistido
        // em aihub.operational_memory reflita o output FINAL emitido ao usuário,
        // não o output bruto do LLM.
        current = WrapMiddlewares(current, definition, IsPreMemoryPhase);

        // Memória operacional fica entre PRE-MEMORY e POST-MEMORY phases:
        // tokens da injeção pré-call são contabilizados; output strippado é o que
        // os demais middlewares (e o Blocklist) veem.
        current = WrapWithOperationalMemory(current, definition, isStandaloneFlow);

        // POST-MEMORY phase: middlewares legados (AccountGuard, StructuredOutputState,
        // SecurityGuardrails) — veem output já strippado de operationalMemory e
        // o STATE_DELTA do StructuredOutputState carrega o payload final.
        current = WrapMiddlewares(current, definition, t => !IsPreMemoryPhase(t));

        // Blocklist mais externo que TokenTracking + middlewares opt-in. Input bloqueado
        // não consome token; output bloqueado conta tokens (já consumidos pelo provider).
        current = WrapWithBlocklist(current, definition.Id);

        if (_circuitBreaker is not null)
        {
            var providerKey = $"{definition.Provider.Type}:{definition.Provider.Endpoint ?? "default"}";

            // Fallback: só se explicitamente configurado e de tipo diferente do primary.
            IChatClient? fallbackClient = null;
            string? fallbackProviderType = null;
            if (definition.FallbackProvider is { } fb
                && !fb.Type.Equals(definition.Provider.Type, StringComparison.OrdinalIgnoreCase))
            {
                if (_providers.TryGetValue(fb.Type, out var fallbackProvider))
                {
                    var fallbackDef = CopyWithProvider(definition, fb);
                    fallbackClient = await fallbackProvider.CreateChatClientAsync(fallbackDef, ct);
                    fallbackProviderType = fb.Type;
                }
            }

            current = new CircuitBreakerChatClient(
                current, _circuitBreaker, providerKey, _logger,
                fallbackClient, fallbackProviderType);
        }

        return new RetryingChatClient(current, definition.Id, modelId, _logger, definition.Resilience);
    }

    /// <summary>
    /// Aplica apenas os middlewares do agente (ex: AccountGuard, StructuredOutputState).
    /// Usado pelo CreateLlmHandlerAsync (Graph mode) que já faz token tracking manual.
    /// Mesmas duas fases do <c>WrapWithTokenTrackingAsync</c>: pré-memória pra
    /// hard validators que mutam output, pós-memória pros demais.
    /// </summary>
    private IChatClient WrapWithMiddlewares(
        IChatClient inner, AgentDefinition definition, bool isStandaloneFlow = false)
    {
        IChatClient current = WrapMiddlewares(inner, definition, IsPreMemoryPhase);
        current = WrapWithOperationalMemory(current, definition, isStandaloneFlow);
        current = WrapMiddlewares(current, definition, t => !IsPreMemoryPhase(t));
        // Blocklist também no Graph mode — coberto independente do pipeline ser via
        // WrapWithTokenTracking ou direto via CreateLlmHandlerAsync.
        current = WrapWithBlocklist(current, definition.Id);
        return current;
    }

    /// <summary>
    /// Helper compartilhado: itera <see cref="AgentDefinition.Middlewares"/>
    /// filtrando por <paramref name="typePredicate"/> e wrappa cada entry
    /// habilitada. Preserva ordem do array — primeira entry filtrada fica mais
    /// interna no resultado.
    /// </summary>
    private IChatClient WrapMiddlewares(
        IChatClient inner, AgentDefinition definition, Func<string, bool> typePredicate)
    {
        var current = inner;
        foreach (var mw in definition.Middlewares.Where(m => m.Enabled && typePredicate(m.Type)))
        {
            if (!_middlewareRegistry.TryCreate(mw.Type, current, definition.Id, mw.Settings, _logger, out var wrapped))
                LogAndSkipMiddleware(current, mw.Type, definition.Id);
            else
                current = wrapped;
        }
        return current;
    }

    /// <summary>
    /// Plug do BlocklistChatClient quando engine está disponível (sempre em produção;
    /// null em testes unitários que não injetam o engine). No-op silencioso quando null.
    /// </summary>
    private IChatClient WrapWithBlocklist(IChatClient inner, string agentId)
    {
        if (_blocklistEngine is null) return inner;
        return new BlocklistChatClient(inner, _blocklistEngine, _eventBus, _auditLogger, agentId, _logger);
    }

    /// <summary>
    /// Plug do <see cref="OperationalMemoryChatClient"/> quando o agent declara
    /// schema de memória e o repo está disponível em DI. No-op silencioso quando
    /// faltar qualquer um dos dois (preserva BC com agentes antigos e testes
    /// que não injetam o repo).
    ///
    /// Em workflow standalone (<paramref name="isStandaloneFlow"/> = true) o
    /// middleware é desligado: sem continuidade entre chamadas, persistir
    /// memória só polui o DB com rows efêmeras. O ResponseFormat também é
    /// recomposto sem o campo <c>operationalMemory</c> em <see cref="ChatOptionsBuilder"/>,
    /// então o LLM nem gera o campo — economiza tokens.
    /// </summary>
    private IChatClient WrapWithOperationalMemory(
        IChatClient inner, AgentDefinition definition, bool isStandaloneFlow = false)
    {
        if (definition.OperationalMemory?.Schema is null) return inner;
        if (isStandaloneFlow)
        {
            _logger.LogDebug(
                "[AgentFactory] Agent '{AgentId}': workflow standalone — operational memory desligada (bypass middleware + schema).",
                definition.Id);
            return inner;
        }
        if (_operationalMemoryRepo is null)
        {
            _logger.LogWarning(
                "Agent '{AgentId}' declara OperationalMemory mas IOperationalMemoryRepository não está em DI — middleware ignorado.",
                definition.Id);
            return inner;
        }

        var maxBytes = definition.OperationalMemory.MaxBytes
            ?? AgentOperationalMemoryDefinition.DefaultMaxBytes;
        return new EfsAiHub.Platform.Runtime.Middlewares.OperationalMemoryChatClient(
            inner, definition.Id, _operationalMemoryRepo, maxBytes, _logger);
    }

    private IChatClient LogAndSkipMiddleware(IChatClient current, string type, string agentId)
    {
        _logger.LogWarning("Unknown middleware type '{Type}' on agent '{AgentId}' — ignored.", type, agentId);
        return current;
    }

    /// <summary>
    /// Encontra o texto da última mensagem com Role=User na lista enviada ao LLM.
    /// Usado pelo bypass de Router Quick Action — ignora system prompts e turnos
    /// anteriores. Retorna null/empty se não há user message (caso defensivo).
    /// </summary>
    private static string? ExtractLastUserText(IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User)
                return messages[i].Text;
        }
        return null;
    }

    private static AgentDefinition CopyWithInstructions(AgentDefinition d, string instructions) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description,
        // Campos chave de tipologia que precisam atravessar todas as cópias:
        // Type rege a injeção de blocos em runtime (RouterIntent / WorkerScope);
        // RouterIntentIds é o set transient do save que o builder consome.
        // Sem propagação, o agent chega ao runtime degradado pra Custom.
        Type = d.Type,
        RouterIntentIds = d.RouterIntentIds,
        Model = d.Model,
        Provider = d.Provider,
        FallbackProvider = d.FallbackProvider,
        AuthorInstructions = d.AuthorInstructions,
        Instructions = instructions,
        Tools = d.Tools,
        StructuredOutput = d.StructuredOutput,
        OperationalMemory = d.OperationalMemory,
        Middlewares = d.Middlewares,
        Resilience = d.Resilience,
        CostBudget = d.CostBudget,
        SkillRefs = d.SkillRefs,
        Metadata = d.Metadata,
        ProjectId = d.ProjectId,
        TenantId = d.TenantId,
        Visibility = d.Visibility,
        AllowedProjectIds = d.AllowedProjectIds,
        Enabled = d.Enabled,
        RegressionTestSetId = d.RegressionTestSetId,
        RegressionEvaluatorConfigVersionId = d.RegressionEvaluatorConfigVersionId,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

    /// <summary>
    /// Sobrescreve ApiKey/Endpoint do agente com as credenciais do projeto, se definidas.
    /// Usa fallback gracioso: se o projeto não tiver credenciais para o provider, a definição original é mantida.
    /// </summary>
    private async Task<AgentDefinition> InjectProjectCredentials(AgentDefinition definition, CancellationToken ct)
    {
        if (_projectRepo is null || string.IsNullOrEmpty(definition.ProjectId))
            return definition;

        var project = await _projectRepo.GetByIdAsync(definition.ProjectId, ct);
        if (project?.LlmConfig?.Credentials is not { Count: > 0 } creds)
            return definition;

        if (!creds.TryGetValue(definition.Provider.Type.ToUpperInvariant(), out var projectCred))
            return definition;

        if (string.IsNullOrEmpty(projectCred.ApiKey) && string.IsNullOrEmpty(projectCred.Endpoint))
            return definition;

        _logger.LogInformation(
            "[AgentFactory] Using project-level credentials for provider {Provider} (project: {ProjectId}, agent: {AgentId})",
            definition.Provider.Type, definition.ProjectId, definition.Id);

        var overriddenProvider = new AgentProviderConfig
        {
            Type       = definition.Provider.Type,
            ClientType = definition.Provider.ClientType,
            ApiKey     = !string.IsNullOrEmpty(projectCred.ApiKey)  ? projectCred.ApiKey  : definition.Provider.ApiKey,
            Endpoint   = !string.IsNullOrEmpty(projectCred.Endpoint) ? projectCred.Endpoint : definition.Provider.Endpoint
        };

        return CopyWithProvider(definition, overriddenProvider);
    }

    /// <summary>Cria cópia com provider substituído (para fallback circuit breaker).</summary>
    private static AgentDefinition CopyWithProvider(AgentDefinition d, AgentProviderConfig provider) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description,
        // Type precisa ser preservado nas cópias do runtime — caso contrário
        // Router vira Custom silenciosamente quando InjectProjectCredentials
        // ou fallback do circuit breaker recriam o definition.
        Type = d.Type,
        Model = d.Model,
        Provider = provider,
        AuthorInstructions = d.AuthorInstructions,
        Instructions = d.Instructions,
        Tools = d.Tools,
        StructuredOutput = d.StructuredOutput,
        OperationalMemory = d.OperationalMemory,
        Middlewares = d.Middlewares,
        Metadata = d.Metadata,
        ProjectId = d.ProjectId,
        TenantId = d.TenantId,
        Visibility = d.Visibility,
        AllowedProjectIds = d.AllowedProjectIds,
        Enabled = d.Enabled,
        RouterIntentIds = d.RouterIntentIds,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

}
