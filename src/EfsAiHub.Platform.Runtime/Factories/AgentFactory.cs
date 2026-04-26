using System.Diagnostics;
using EfsAiHub.Core.Abstractions.AgUi;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Platform.Runtime.Execution;
using EfsAiHub.Platform.Runtime.Guards;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Orquestra a criação de agentes resolvendo o provider LLM correto,
/// construindo as opções de chat e aplicando decorators de middleware/rastreamento de tokens.
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IReadOnlyDictionary<string, ILlmClientProvider> _providers;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentPromptRepository _promptRepo;
    private readonly IFunctionToolRegistry _functionRegistry;
    private readonly IAgentMiddlewareRegistry _middlewareRegistry;
    private readonly ITokenUsageSink _tokenPersistence;
    private readonly IToolInvocationSink _toolPersistence;
    private readonly ILogger<AgentFactory> _logger;
    private readonly ILogger<TrackedAIFunction> _trackedFnLogger;
    private readonly IModelPricingCache? _pricingCache;
    private readonly ISkillResolver? _skillResolver;
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

    public AgentFactory(
        IEnumerable<ILlmClientProvider> providers,
        IAgentDefinitionRepository agentRepo,
        IAgentPromptRepository promptRepo,
        IFunctionToolRegistry functionRegistry,
        IAgentMiddlewareRegistry middlewareRegistry,
        ITokenUsageSink tokenPersistence,
        IToolInvocationSink toolPersistence,
        ILogger<AgentFactory> logger,
        ILogger<TrackedAIFunction> trackedFnLogger,
        IModelPricingCache? pricingCache = null,
        ISkillResolver? skillResolver = null,
        IOptions<WorkflowEngineOptions>? engineOptions = null,
        LlmCircuitBreaker? circuitBreaker = null,
        IAgUiTokenSink? agUiTokenSink = null,
        IProjectRepository? projectRepo = null,
        IAgentVersionRepository? agentVersionRepo = null,
        IPersonaPromptComposer? personaComposer = null,
        ISystemMessageBuilder? systemMessageBuilder = null,
        BlocklistEngine? blocklistEngine = null,
        IWorkflowEventBus? eventBus = null,
        EfsAiHub.Core.Abstractions.Observability.IAdminAuditLogger? auditLogger = null)
    {
        _providers = providers.ToDictionary(p => p.ProviderType, StringComparer.OrdinalIgnoreCase);
        _agentRepo = agentRepo;
        _promptRepo = promptRepo;
        _functionRegistry = functionRegistry;
        _middlewareRegistry = middlewareRegistry;
        _tokenPersistence = tokenPersistence;
        _toolPersistence = toolPersistence;
        _logger = logger;
        _trackedFnLogger = trackedFnLogger;
        _pricingCache = pricingCache;
        _skillResolver = skillResolver;
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
    }

    public async Task<ExecutableWorkflow> CreateAgentAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating agent '{AgentName}' (id: {AgentId}, provider: {Provider}/{ClientType})",
            definition.Name, definition.Id,
            definition.Provider.Type, definition.Provider.ClientType);

        DelegateExecutor.CurrentLogger.Value = _logger;

        definition = await InjectProjectCredentials(definition, ct);
        definition = await ResolveActivePrompt(definition, ct);
        definition = await ResolveSkills(definition, ct);
        await TrackAgentVersionAsync(definition.Id, ct);
        var provider = ResolveProvider(definition);
        var options = ChatOptionsBuilder.BuildAgentOptions(definition, _functionRegistry, _toolPersistence.Writer, _trackedFnLogger, _logger, _allowFingerprintMismatch, projectId: definition.ProjectId);

        if (provider.ProviderType is "AZUREOPENAI" or "OPENAI")
        {
            var rawClient = provider.CreateChatClient(definition);
            var wrappedClient = WrapWithTokenTracking(rawClient, definition);
            return ExecutableWorkflow.FromAgent(wrappedClient.AsAIAgent(options));
        }

        return ExecutableWorkflow.FromAgent(await provider.CreateAgentAsync(definition, options, ct));
    }

    public async Task<IReadOnlyDictionary<string, ExecutableWorkflow>> CreateAgentsForWorkflowAsync(
        WorkflowDefinition workflow, CancellationToken ct = default)
    {
        var result = new Dictionary<string, ExecutableWorkflow>();

        foreach (var agentRef in workflow.Agents)
        {
            var definition = await _agentRepo.GetByIdAsync(agentRef.AgentId, ct)
                ?? throw new InvalidOperationException(
                    $"Agent '{agentRef.AgentId}' referenced in workflow '{workflow.Id}' not found.");

            result[agentRef.AgentId] = await CreateAgentAsync(definition, ct);
        }

        return result;
    }

    public async Task<Func<string, CancellationToken, Task<string>>> CreateLlmHandlerAsync(
        string agentId, CancellationToken ct = default)
    {
        var definition = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent '{agentId}' not found.");

        definition = await InjectProjectCredentials(definition, ct);
        var agentVersionId = await TrackAgentVersionAsync(agentId, ct);
        var provider = ResolveProvider(definition);
        var rawChatClient = provider.CreateChatClient(definition);
        var chatOptions = ChatOptionsBuilder.BuildGraphChatOptions(definition, _functionRegistry, _toolPersistence.Writer, _trackedFnLogger, _logger, _allowFingerprintMismatch, projectId: definition.ProjectId);

        // Envolve com FunctionInvokingChatClient para tratar chamadas de ferramentas automaticamente no modo Graph.
        // Sem isso, o handler lê apenas response.Text, que fica vazio quando o modelo retorna uma tool call.
        // O wrapper faz o loop: LLM→ferramenta→LLM até o modelo produzir uma resposta de texto final.
        IChatClient chatClient = chatOptions.Tools is { Count: > 0 }
            ? new FunctionInvokingChatClient(rawChatClient) { MaximumIterationsPerRequest = 10 }
            : rawChatClient;

        chatClient = WrapWithMiddlewares(chatClient, definition);

        var promptResult = await _promptRepo.GetActivePromptWithVersionAsync(definition.Id, ct);
        var instructions = promptResult?.Content ?? definition.Instructions;
        var promptVersionId = promptResult?.VersionId;
        if (promptVersionId is not null)
            DelegateExecutor.Current.Value?.PromptVersions.TryAdd(definition.Id, promptVersionId);

        var modelId = definition.Model.DeploymentName ?? "unknown";
        var usageWriter = _tokenPersistence.Writer;
        var logger = _logger;

        return async (input, cancellationToken) =>
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
            var expanded = ChatTurnContextMapper.TryExpand(input, composedPersona.UserReinforcement);
            if (expanded is not null)
                messages.AddRange(expanded);
            else
            {
                var userText = composedPersona.UserReinforcement is null
                    ? input
                    : $"{input}\n\n{composedPersona.UserReinforcement}";
                messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userText));
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
        };
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
    /// Resolve a versão Published mais recente do agente e registra no ExecutionContext.AgentVersions.
    /// Retorna o AgentVersionId para uso em LlmTokenUsage (Graph mode).
    /// </summary>
    private async Task<string?> TrackAgentVersionAsync(string agentId, CancellationToken ct)
    {
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

    private async Task<AgentDefinition> ResolveActivePrompt(AgentDefinition definition, CancellationToken ct)
    {
        var promptResult = await _promptRepo.GetActivePromptWithVersionAsync(definition.Id, ct);
        if (promptResult is null) return definition;

        DelegateExecutor.Current.Value?.PromptVersions.TryAdd(definition.Id, promptResult.Value.VersionId);
        return CopyWithInstructions(definition, promptResult.Value.Content);
    }

    private async Task<AgentDefinition> ResolveSkills(AgentDefinition definition, CancellationToken ct)
    {
        if (_skillResolver is null || definition.SkillRefs.Count == 0) return definition;

        var resolved = new List<Skill>(definition.SkillRefs.Count);
        foreach (var skillRef in definition.SkillRefs)
        {
            var skill = await _skillResolver.ResolveAsync(skillRef, ct);
            if (skill is not null)
                resolved.Add(skill);
            else
                _logger.LogWarning(
                    "Skill '{SkillId}' (version={VersionId}) referenced by agent '{AgentId}' not found — ignored.",
                    skillRef.SkillId, skillRef.SkillVersionId, definition.Id);
        }

        return SkillMerger.ApplySkills(definition, resolved);
    }

    private IChatClient WrapWithTokenTracking(IChatClient inner, AgentDefinition definition)
    {
        var modelId = definition.Model.DeploymentName ?? "unknown";

        // Cadeia: Retry → Circuit → Blocklist → [AccountGuard etc] → TokenTracking → Raw
        IChatClient current = new TokenTrackingChatClient(inner, definition.Id, modelId, _tokenPersistence.Writer, _logger, _pricingCache, _agUiTokenSink);

        foreach (var mw in definition.Middlewares.Where(m => m.Enabled))
        {
            if (!_middlewareRegistry.TryCreate(mw.Type, current, definition.Id, mw.Settings, _logger, out var wrapped))
                LogAndSkipMiddleware(current, mw.Type, definition.Id);
            else
                current = wrapped;
        }

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
                    fallbackClient = fallbackProvider.CreateChatClient(fallbackDef);
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
    /// </summary>
    private IChatClient WrapWithMiddlewares(IChatClient inner, AgentDefinition definition)
    {
        IChatClient current = inner;
        foreach (var mw in definition.Middlewares.Where(m => m.Enabled))
        {
            if (!_middlewareRegistry.TryCreate(mw.Type, current, definition.Id, mw.Settings, _logger, out var wrapped))
                LogAndSkipMiddleware(current, mw.Type, definition.Id);
            else
                current = wrapped;
        }
        // Blocklist também no Graph mode — coberto independente do pipeline ser via
        // WrapWithTokenTracking ou direto via CreateLlmHandlerAsync.
        current = WrapWithBlocklist(current, definition.Id);
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

    private IChatClient LogAndSkipMiddleware(IChatClient current, string type, string agentId)
    {
        _logger.LogWarning("Unknown middleware type '{Type}' on agent '{AgentId}' — ignored.", type, agentId);
        return current;
    }

    private static AgentDefinition CopyWithInstructions(AgentDefinition d, string instructions) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description,
        Model = d.Model,
        Provider = d.Provider,
        FallbackProvider = d.FallbackProvider,
        Instructions = instructions,
        Tools = d.Tools,
        StructuredOutput = d.StructuredOutput,
        Middlewares = d.Middlewares,
        Metadata = d.Metadata,
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
        Model = d.Model,
        Provider = provider,
        Instructions = d.Instructions,
        Tools = d.Tools,
        StructuredOutput = d.StructuredOutput,
        Middlewares = d.Middlewares,
        Metadata = d.Metadata,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };

}
