using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Abstractions.AgentSandbox;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Execution;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Interfaces;
using EfsAiHub.Core.Orchestration.Validation;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.AgentSandbox;

/// <summary>
/// Orquestra ciclo de vida de sandbox sessions de agente: criação (workflow
/// efêmero + conversation quando aplicável + session record), listagem,
/// fechamento manual, validação (gate de Chat) e predição stateless de
/// intent (Router).
///
/// <para>
/// Backend decide <c>Mode</c> por <see cref="AgentType"/>:
/// Conversational → Chat workflow + conversation (InputMode=Chat, Graph);
/// Custom/Worker/ToolRunner → Standalone workflow single-shot (sem
/// conversation); Router → não cria session, usa <c>PredictRouterIntentAsync</c>.
/// </para>
///
/// Pin exato da AgentVersion é alimentado via metadata do workflow efêmero;
/// <c>WorkflowExecutor</c> ativa <c>exactAgentPin</c> quando
/// <c>execution.WorkflowVersionId</c> vem setado.
/// </summary>
public sealed class AgentSandboxService
{
    private readonly IAgentSandboxSessionRepository _sandboxRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository? _agentVersionRepo;
    private readonly IWorkflowService _workflowService;
    private readonly IConversationLifecycle _conversationLifecycle;
    private readonly IChatMessageRepository _messageRepo;
    private readonly IAgentFactory? _agentFactory;
    private readonly IAdminAuditLogger? _auditLogger;
    private readonly AdminAuditContext? _auditContext;
    private readonly IProjectContextAccessor _projectAccessor;
    private readonly IOptions<AgentSandboxOptions> _options;
    private readonly ILogger<AgentSandboxService> _logger;

    public AgentSandboxService(
        IAgentSandboxSessionRepository sandboxRepo,
        IAgentDefinitionRepository agentRepo,
        IWorkflowService workflowService,
        IConversationLifecycle conversationLifecycle,
        IChatMessageRepository messageRepo,
        IProjectContextAccessor projectAccessor,
        IOptions<AgentSandboxOptions> options,
        ILogger<AgentSandboxService> logger,
        IAgentVersionRepository? agentVersionRepo = null,
        IAgentFactory? agentFactory = null,
        IAdminAuditLogger? auditLogger = null,
        AdminAuditContext? auditContext = null)
    {
        _sandboxRepo = sandboxRepo;
        _agentRepo = agentRepo;
        _workflowService = workflowService;
        _conversationLifecycle = conversationLifecycle;
        _messageRepo = messageRepo;
        _projectAccessor = projectAccessor;
        _auditContext = auditContext;
        _options = options;
        _logger = logger;
        _agentVersionRepo = agentVersionRepo;
        _agentFactory = agentFactory;
        _auditLogger = auditLogger;
    }

    public sealed record CreateSessionRequest(string? AgentVersionId);

    public async Task<AgentSandboxSession> CreateSessionAsync(
        string agentId,
        UserContext caller,
        CreateSessionRequest? request,
        CancellationToken ct = default)
    {
        var agent = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new KeyNotFoundException($"Agente '{agentId}' não encontrado.");

        if (!agent.Enabled)
            throw new InvalidOperationException(
                $"Agente '{agentId}' está desabilitado — habilite antes de criar uma session.");

        // Router é classificador puro: sandbox isolada exige branches reais ou
        // mock pra fazer sentido. Caminho dedicado é /predict-intent (stateless).
        if (agent.Type == AgentType.Router)
            throw new InvalidOperationException(
                "Router não suporta Sandbox session — use POST /api/aihub/agents/{id}/predict-intent " +
                "pra testar a classificação de intent isoladamente.");

        var resolvedVersionId = await ResolveAgentVersionIdAsync(agentId, request?.AgentVersionId, ct);
        var sandboxSessionId = Guid.NewGuid().ToString("N");

        // Backend decide o mode por agent.Type (regra de negócio). Frontend roteia
        // pela resposta — nunca hardcoda mapping. Conversational → Chat (cria
        // conversation, AG-UI); demais → Standalone (single-shot, sem conversation).
        if (agent.Type == AgentType.Conversational)
            return await CreateChatSessionAsync(agent, resolvedVersionId, sandboxSessionId, caller, ct);
        return await CreateStandaloneSessionAsync(agent, resolvedVersionId, sandboxSessionId, caller, ct);
    }

    private async Task<AgentSandboxSession> CreateChatSessionAsync(
        AgentDefinition agent,
        string resolvedVersionId,
        string sandboxSessionId,
        UserContext caller,
        CancellationToken ct)
    {
        var workflowId = $"deploy-chat-sandbox-{sandboxSessionId[..8]}";

        var workflowDefinition = BuildChatSandboxWorkflow(
            workflowId, agent, resolvedVersionId, sandboxSessionId, caller.UserId);

        var createdWorkflow = await _workflowService.CreateAsync(workflowDefinition, ct);

        var conversation = await _conversationLifecycle.CreateAsync(
            workflowId: createdWorkflow.Id,
            userId: caller.UserId,
            userType: caller.UserType,
            metadata: new Dictionary<string, string>
            {
                [AgentSandboxMetadata.SessionIdKey] = sandboxSessionId,
                ["agentId"] = agent.Id,
                ["kind"] = AgentSandboxMetadata.KindChatSandbox,
            },
            ct: ct);

        var now = DateTime.UtcNow;
        var session = new AgentSandboxSession
        {
            SandboxSessionId = sandboxSessionId,
            AgentId = agent.Id,
            AgentVersionId = resolvedVersionId,
            Mode = AgentSandboxModes.Chat,
            WorkflowId = createdWorkflow.Id,
            ConversationId = conversation.ConversationId,
            ProjectId = _projectAccessor.Current.ProjectId,
            CreatedByUserId = caller.UserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.Value.SessionTtlDays),
            Status = AgentSandboxSessionStatus.Active,
        };

        await _sandboxRepo.CreateAsync(session, ct);

        // Schema do payload preserva o nome legado <c>chatSandboxSessionId</c>
        // pra que queries existentes sobre admin_audit_log continuem batendo
        // (rows pré-rename usam essa chave). Campo <c>mode</c> é novo e seguro
        // pra adicionar — readers que não conhecem ignoram.
        await TryAuditAsync(
            AdminAuditActions.ChatSandboxSessionCreated,
            AdminAuditResources.ChatSandboxSession,
            session.SandboxSessionId,
            payloadAfter: new
            {
                chatSandboxSessionId = session.SandboxSessionId,
                agentId = session.AgentId,
                agentVersionId = session.AgentVersionId,
                mode = session.Mode,
                workflowId = session.WorkflowId,
                conversationId = session.ConversationId,
            },
            ct);

        _logger.LogInformation(
            "[AgentSandbox] Chat session '{SessionId}' criada (agent={AgentId}@{VersionId}, workflow={WorkflowId}).",
            sandboxSessionId, agent.Id, resolvedVersionId, createdWorkflow.Id);

        return session;
    }

    private async Task<AgentSandboxSession> CreateStandaloneSessionAsync(
        AgentDefinition agent,
        string resolvedVersionId,
        string sandboxSessionId,
        UserContext caller,
        CancellationToken ct)
    {
        var workflowId = $"sandbox-standalone-{sandboxSessionId[..8]}";

        var workflowDefinition = BuildStandaloneSandboxWorkflow(
            workflowId, agent, resolvedVersionId, sandboxSessionId, caller.UserId);

        var createdWorkflow = await _workflowService.CreateAsync(workflowDefinition, ct);

        // Standalone não cria conversation: cada trigger é single-shot, sem
        // histórico AG-UI. DeploymentSandbox.tsx (UI) renderiza via SSE de
        // execution direto.

        var now = DateTime.UtcNow;
        var session = new AgentSandboxSession
        {
            SandboxSessionId = sandboxSessionId,
            AgentId = agent.Id,
            AgentVersionId = resolvedVersionId,
            Mode = AgentSandboxModes.Standalone,
            WorkflowId = createdWorkflow.Id,
            ConversationId = null,
            ProjectId = _projectAccessor.Current.ProjectId,
            CreatedByUserId = caller.UserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.Value.SessionTtlDays),
            Status = AgentSandboxSessionStatus.Active,
        };

        await _sandboxRepo.CreateAsync(session, ct);

        await TryAuditAsync(
            AdminAuditActions.AgentSandboxStandaloneSessionCreated,
            AdminAuditResources.AgentSandboxSession,
            session.SandboxSessionId,
            payloadAfter: new
            {
                sandboxSessionId = session.SandboxSessionId,
                agentId = session.AgentId,
                agentType = agent.Type.ToString(),
                agentVersionId = session.AgentVersionId,
                mode = session.Mode,
                workflowId = session.WorkflowId,
            },
            ct);

        _logger.LogInformation(
            "[AgentSandbox] Standalone session '{SessionId}' criada (agent={AgentId}/{Type}@{VersionId}, workflow={WorkflowId}).",
            sandboxSessionId, agent.Id, agent.Type, resolvedVersionId, createdWorkflow.Id);

        return session;
    }

    public Task<AgentSandboxSession?> GetByIdAsync(string sessionId, CancellationToken ct = default)
        => _sandboxRepo.GetByIdAsync(sessionId, ct);

    public Task<IReadOnlyList<AgentSandboxSession>> ListByAgentAsync(
        string agentId,
        AgentSandboxSessionStatus? statusFilter,
        int limit,
        CancellationToken ct = default)
        => _sandboxRepo.ListByAgentAsync(agentId, statusFilter, limit, ct);

    public async Task CloseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sandboxRepo.GetByIdAsync(sessionId, ct)
            ?? throw new KeyNotFoundException($"AgentSandboxSession '{sessionId}' não encontrada.");

        if (session.Status is AgentSandboxSessionStatus.Validated)
            throw new InvalidOperationException(
                "Sessions validadas não podem ser fechadas — preservam audit trail.");

        if (session.Status is AgentSandboxSessionStatus.Closed or AgentSandboxSessionStatus.Expired)
            return;

        session.Status = AgentSandboxSessionStatus.Closed;
        await _sandboxRepo.UpdateAsync(session, ct);

        _logger.LogInformation("[AgentSandbox] Session '{SessionId}' fechada.", sessionId);
    }

    public sealed record ValidateSessionRequest(string? Notes);

    /// <summary>
    /// Promove a session a <c>Validated</c> e popula
    /// <c>agent_definitions.LastChatSandboxValidated*</c>. Gate pra que Chat
    /// deploys parem de avisar "agent não validado" pra essa versão. Exige
    /// ≥1 mensagem na conversation. Em V1 só funciona pra <c>Mode=chat</c>;
    /// chamadas pra session standalone retornam 400 (sem consumer downstream).
    /// </summary>
    public async Task<AgentSandboxSession> ValidateAsync(
        string sessionId,
        UserContext caller,
        ValidateSessionRequest? request,
        CancellationToken ct = default)
    {
        var session = await _sandboxRepo.GetByIdAsync(sessionId, ct)
            ?? throw new KeyNotFoundException($"AgentSandboxSession '{sessionId}' não encontrada.");

        if (!string.Equals(session.Mode, AgentSandboxModes.Chat, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Validation gate está disponível apenas pra sessions de Chat (Conversational). " +
                $"Mode atual: '{session.Mode}'.");

        if (session.Status != AgentSandboxSessionStatus.Active)
            throw new InvalidOperationException(
                $"Apenas sessions Active podem ser validadas. Estado atual: {session.Status}.");

        if (string.IsNullOrEmpty(session.ConversationId))
            throw new InvalidOperationException(
                "Session de Chat sem ConversationId — estado inconsistente, não pode ser validada.");

        var sample = await _messageRepo.ListAsync(session.ConversationId, limit: 1, offset: 0, ct);
        if (sample.Count == 0)
            throw new InvalidOperationException(
                "Session sem mensagens não pode ser validada. Envie ao menos um turn de teste primeiro.");

        var now = DateTime.UtcNow;
        var updated = await _agentRepo.SetChatSandboxValidationAsync(
            session.AgentId, now, caller.UserId, session.AgentVersionId, ct);
        if (!updated)
            throw new InvalidOperationException(
                $"Agente '{session.AgentId}' não foi encontrado pra gravar validation — pode ter sido deletado.");

        session.Status = AgentSandboxSessionStatus.Validated;
        session.ValidatedAt = now;
        session.ValidatedByUserId = caller.UserId;
        session.ValidationNotes = request?.Notes;
        await _sandboxRepo.UpdateAsync(session, ct);

        await TryAuditAsync(
            AdminAuditActions.ChatSandboxValidated,
            AdminAuditResources.ChatSandboxSession,
            session.SandboxSessionId,
            payloadAfter: new
            {
                chatSandboxSessionId = session.SandboxSessionId,
                agentId = session.AgentId,
                agentVersionId = session.AgentVersionId,
                validatedByUserId = caller.UserId,
                notes = request?.Notes,
            },
            ct);

        _logger.LogInformation(
            "[AgentSandbox] Session '{SessionId}' validada por '{UserId}' (agent={AgentId}@{VersionId}).",
            sessionId, caller.UserId, session.AgentId, session.AgentVersionId);

        return session;
    }

    private async Task<string> ResolveAgentVersionIdAsync(
        string agentId, string? requestedVersionId, CancellationToken ct)
    {
        if (_agentVersionRepo is null)
            throw new InvalidOperationException(
                "IAgentVersionRepository não está disponível — versioning é requisito do Agent Sandbox.");

        if (!string.IsNullOrEmpty(requestedVersionId))
        {
            var requested = await _agentVersionRepo.GetByIdAsync(requestedVersionId, ct)
                ?? throw new ArgumentException(
                    $"AgentVersion '{requestedVersionId}' não encontrada.");
            if (!string.Equals(requested.AgentDefinitionId, agentId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"AgentVersion '{requestedVersionId}' não pertence ao agent '{agentId}'.");
            return requested.AgentVersionId;
        }

        var current = await _agentVersionRepo.GetCurrentAsync(agentId, ct)
            ?? throw new InvalidOperationException(
                $"Agente '{agentId}' não tem nenhuma AgentVersion publicada. " +
                "Versionamento é requisito pra testar (sandbox ou predict-intent). " +
                "Edite e republique o agente pelo fluxo de aprovação ou execute o backfill " +
                "de versões se ele foi inserido via seed.");
        return current.AgentVersionId;
    }

    private WorkflowDefinition BuildChatSandboxWorkflow(
        string workflowId,
        AgentDefinition agent,
        string agentVersionId,
        string sandboxSessionId,
        string createdByUserId)
    {
        // Conversational depende de StructuredOutputState (alimentação do AG-UI
        // shared state) + InputMode=Chat. Metadata marca a origem pra que
        // listagens de "deploys reais" e cleanup background distinguam dos
        // workflows oficiais.
        return new WorkflowDefinition
        {
            Id = workflowId,
            Name = $"Chat Sandbox · {agent.Name}",
            Description = $"Workflow efêmero pra teste isolado do Conversational '{agent.Name}'.",
            OrchestrationMode = OrchestrationMode.Graph,
            ProjectId = _projectAccessor.Current.ProjectId,
            Visibility = "project",
            Configuration = new WorkflowConfiguration
            {
                InputMode = "Chat",
                TimeoutSeconds = 300,
                MaxRounds = null,
                MaxAgentInvocations = 10,
                MaxHistoryMessages = 50,
                MaxTokensPerExecution = 50000,
                CheckpointMode = "InMemory",
                EnableHumanInTheLoop = false,
                ExposeAsAgent = false,
            },
            Agents =
            [
                new WorkflowAgentReference
                {
                    AgentId = agent.Id,
                    AgentVersionId = agentVersionId,
                    Role = "EntryPoint",
                },
            ],
            Metadata = new Dictionary<string, string>
            {
                [AgentSandboxMetadata.DeploymentKindKey] = AgentSandboxMetadata.DeploymentKindChat,
                [AgentSandboxMetadata.KindKey] = AgentSandboxMetadata.KindChatSandbox,
                [AgentSandboxMetadata.TransientKey] = "true",
                [AgentSandboxMetadata.SessionIdKey] = sandboxSessionId,
                ["createdByUserId"] = createdByUserId,
                ["deployedFromAgentId"] = agent.Id,
            },
        };
    }

    public sealed record RouterPredictRequest(string Input, string? AgentVersionId);

    public sealed record RouterPredictResult(
        string Intent,
        string? Reasoning,
        string RawOutput,
        long LatencyMs,
        string AgentVersionId);

    /// <summary>
    /// Classificação stateless do intent por agente Router. Não cria sandbox
    /// session — não há workflow nem persistência. Chama o LLM diretamente
    /// via <see cref="IAgentFactory"/> + <see cref="AIAgent.RunAsync"/>.
    /// <para>
    /// Output esperado segue o schema canônico de Router (<c>intent</c> enum
    /// + <c>reasoning</c> opcional). Se o LLM cuspir JSON malformado, devolve
    /// <c>RawOutput</c> + <c>Intent="unknown"</c> em vez de explodir —
    /// owners testam pra ver justamente esse caso.
    /// </para>
    /// </summary>
    public async Task<RouterPredictResult> PredictRouterIntentAsync(
        string agentId,
        RouterPredictRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Input))
            throw new ArgumentException("Input é obrigatório pra predict-intent.", nameof(request));

        // Cap defensivo contra payloads abusivos: Router classifica intent, não
        // recebe livros. LLM cap propagaria o erro do provedor com mensagem
        // confusa; aqui rejeitamos antecipadamente com 400 acionável.
        var maxInputChars = _options.Value.PredictIntentMaxInputChars;
        if (maxInputChars > 0 && request.Input.Length > maxInputChars)
            throw new ArgumentException(
                $"Input excede o limite de {maxInputChars} caracteres ({request.Input.Length} recebido).",
                nameof(request));

        if (_agentFactory is null)
            throw new InvalidOperationException(
                "IAgentFactory não está disponível — registro de DI incompleto.");

        var agent = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new KeyNotFoundException($"Agente '{agentId}' não encontrado.");

        if (agent.Type != AgentType.Router)
            throw new InvalidOperationException(
                $"predict-intent só está disponível pra agentes Router (tipo atual: {agent.Type}).");

        if (!agent.Enabled)
            throw new InvalidOperationException(
                $"Agente '{agentId}' está desabilitado — habilite antes de testar.");

        var resolvedVersionId = await ResolveAgentVersionIdAsync(agentId, request.AgentVersionId, ct);

        // Popula DelegateExecutor pra middlewares per-projeto (Blocklist,
        // TokenTracking) terem ProjectId. Padrão mesmo do AgentSessionService.
        var ctx = _projectAccessor.Current;
        if (!ctx.IsExplicit)
            throw new InvalidOperationException(
                "ProjectContext deve ser populado pelo ProjectMiddleware antes de predict-intent.");

        var executionId = Guid.NewGuid().ToString("N");
        DelegateExecutor.Current.Value = new EfsAiHub.Core.Agents.Execution.ExecutionContext(
            ExecutionId: executionId,
            WorkflowId: $"router-predict:{agentId}",
            Input: request.Input,
            PromptVersions: new ConcurrentDictionary<string, string>(),
            NodeCallback: null,
            Budget: new ExecutionBudget(maxTokensPerExecution: 0),
            UserId: null,
            GuardMode: AccountGuardMode.None,
            AgentVersions: new ConcurrentDictionary<string, string>(),
            ProjectId: ctx.ProjectId);

        // Build agent + run single-shot. isStandaloneFlow=true desabilita
        // composição com operationalMemory (Router não tem schema interno).
        var sw = Stopwatch.StartNew();
        var aiAgent = (AIAgent)(await _agentFactory.CreateAgentAsync(agent, ct, isStandaloneFlow: true)).Value;
        var session = await aiAgent.CreateSessionAsync(ct);
        var response = await aiAgent.RunAsync(request.Input, session, cancellationToken: ct);
        sw.Stop();

        var rawOutput = response?.ToString() ?? string.Empty;
        var (intent, reasoning) = TryParseRouterOutput(rawOutput);

        await TryAuditAsync(
            AdminAuditActions.RouterIntentPredicted,
            AdminAuditResources.Agent,
            agentId,
            payloadAfter: new
            {
                agentId,
                agentVersionId = resolvedVersionId,
                inputLength = request.Input.Length,
                intent,
                latencyMs = sw.ElapsedMilliseconds,
            },
            ct);

        _logger.LogInformation(
            "[AgentSandbox] Router '{AgentId}@{VersionId}' classificou intent='{Intent}' em {Latency}ms.",
            agentId, resolvedVersionId, intent, sw.ElapsedMilliseconds);

        return new RouterPredictResult(intent, reasoning, rawOutput, sw.ElapsedMilliseconds, resolvedVersionId);
    }

    /// <summary>
    /// Extrai <c>intent</c>/<c>reasoning</c> do output do Router. Se o JSON
    /// tá malformado ou sem <c>intent</c>, devolve <c>"unknown"</c> em vez de
    /// throw — predict-intent é ferramenta de diagnóstico, owner precisa ver
    /// que o agent gerou lixo.
    /// <para>
    /// Visibilidade <c>internal</c> pra que <c>EfsAiHub.Tests.Unit</c> possa
    /// cobrir edge cases (JSON válido, malformado, sem intent) sem precisar
    /// mockar <see cref="IAgentFactory"/> + LLM. Host.Api expõe via
    /// <c>InternalsVisibleTo</c>.
    /// </para>
    /// </summary>
    internal static (string Intent, string? Reasoning) TryParseRouterOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("unknown", null);
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonObject obj)
            {
                var intent = obj["intent"]?.GetValue<string>();
                var reasoning = obj["reasoning"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(intent))
                    return (intent, reasoning);
            }
        }
        catch (JsonException)
        {
            // JSON inválido — fall through.
        }
        return ("unknown", null);
    }

    private WorkflowDefinition BuildStandaloneSandboxWorkflow(
        string workflowId,
        AgentDefinition agent,
        string agentVersionId,
        string sandboxSessionId,
        string createdByUserId)
    {
        // Custom/Worker/ToolRunner rodam single-shot: input → output sem histórico
        // conversational. OrchestrationMode=Sequential é equivalente a Graph pra
        // um agente único e simplifica diff visual no Sandbox UI.
        // Metadata kind=standalone-sandbox marca pra cleanup background e pra
        // distinguir de deploys Standalone permanentes em listagens analíticas.
        return new WorkflowDefinition
        {
            Id = workflowId,
            Name = $"Standalone Sandbox · {agent.Name}",
            Description = $"Workflow efêmero pra teste isolado de '{agent.Name}' ({agent.Type}).",
            OrchestrationMode = OrchestrationMode.Sequential,
            ProjectId = _projectAccessor.Current.ProjectId,
            Visibility = "project",
            Configuration = new WorkflowConfiguration
            {
                InputMode = "Standalone",
                TimeoutSeconds = 300,
                MaxRounds = null,
                MaxAgentInvocations = 10,
                MaxHistoryMessages = 0,
                MaxTokensPerExecution = 50000,
                CheckpointMode = "InMemory",
                EnableHumanInTheLoop = false,
                ExposeAsAgent = false,
            },
            Agents =
            [
                new WorkflowAgentReference
                {
                    AgentId = agent.Id,
                    AgentVersionId = agentVersionId,
                    Role = "EntryPoint",
                },
            ],
            Metadata = new Dictionary<string, string>
            {
                [AgentSandboxMetadata.DeploymentKindKey] = AgentSandboxMetadata.DeploymentKindStandalone,
                [AgentSandboxMetadata.KindKey] = AgentSandboxMetadata.KindStandaloneSandbox,
                [AgentSandboxMetadata.TransientKey] = "true",
                [AgentSandboxMetadata.SessionIdKey] = sandboxSessionId,
                ["createdByUserId"] = createdByUserId,
                ["deployedFromAgentId"] = agent.Id,
                ["deployedFromAgentType"] = agent.Type.ToString(),
            },
        };
    }

    private async Task TryAuditAsync(
        string action,
        string resourceType,
        string resourceId,
        object payloadAfter,
        CancellationToken ct)
    {
        if (_auditLogger is null || _auditContext is null) return;
        try
        {
            await _auditLogger.RecordAsync(
                _auditContext.Build(
                    action,
                    resourceType,
                    resourceId,
                    payloadAfter: AdminAuditContext.Snapshot(payloadAfter)),
                ct);
        }
        catch (Exception ex)
        {
            // Audit não pode bloquear o fluxo — só loga.
            _logger.LogWarning(ex,
                "[AgentSandbox] Falha ao registrar audit '{Action}' ({ResourceType}={ResourceId}).",
                action, resourceType, resourceId);
        }
    }
}
