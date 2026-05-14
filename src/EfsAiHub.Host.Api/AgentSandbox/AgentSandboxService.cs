using EfsAiHub.Core.Abstractions.AgentSandbox;
using EfsAiHub.Core.Abstractions.Conversations;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Enums;
using EfsAiHub.Core.Orchestration.Interfaces;
using EfsAiHub.Core.Orchestration.Validation;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Host.Api.Services;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Platform.Runtime.Options;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.AgentSandbox;

/// <summary>
/// Orquestra ciclo de vida de sessions de sandbox de agente: criação
/// (workflow efêmero + conversation, quando aplicável + session record),
/// listagem, fechamento manual e validação (gate de Chat).
///
/// <para>
/// Hoje só constrói workflow Chat (<c>Mode=chat</c>) — caminho Standalone
/// pra Custom/Worker/ToolRunner é construído em entrega subsequente.
/// </para>
///
/// Workflow é criado como Chat real (InputMode=Chat, OrchestrationMode=Graph)
/// com pin exato da AgentVersion alvo — o trigger usa header x-version
/// automaticamente porque <c>WorkflowExecutor</c> ativa <c>exactAgentPin</c>
/// quando o execution.WorkflowVersionId vem setado.
/// </summary>
public sealed class AgentSandboxService
{
    private readonly IAgentSandboxSessionRepository _sandboxRepo;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository? _agentVersionRepo;
    private readonly IWorkflowService _workflowService;
    private readonly IConversationLifecycle _conversationLifecycle;
    private readonly IChatMessageRepository _messageRepo;
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

        if (agent.Type != AgentType.Conversational)
            throw new InvalidOperationException(
                $"Chat Sandbox só está disponível pra agentes Conversational (tipo atual: {agent.Type}).");

        if (!agent.Enabled)
            throw new InvalidOperationException(
                $"Agente '{agentId}' está desabilitado — habilite antes de criar uma session.");

        var resolvedVersionId = await ResolveAgentVersionIdAsync(agentId, request?.AgentVersionId, ct);

        var sandboxSessionId = Guid.NewGuid().ToString("N");
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
                ["agentId"] = agentId,
                ["kind"] = AgentSandboxMetadata.KindChatSandbox,
            },
            ct: ct);

        var now = DateTime.UtcNow;
        var session = new AgentSandboxSession
        {
            SandboxSessionId = sandboxSessionId,
            AgentId = agentId,
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
            "[AgentSandbox] Session '{SessionId}' criada (mode={Mode}, agent={AgentId}@{VersionId}, workflow={WorkflowId}).",
            sandboxSessionId, session.Mode, agentId, resolvedVersionId, createdWorkflow.Id);

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
                $"Agente '{agentId}' não tem nenhuma AgentVersion publicada — publique antes de criar sandbox.");
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

    private async Task TryAuditAsync(string action, string resourceId, object payloadAfter, CancellationToken ct)
    {
        if (_auditLogger is null || _auditContext is null) return;
        try
        {
            await _auditLogger.RecordAsync(
                _auditContext.Build(
                    action,
                    AdminAuditResources.ChatSandboxSession,
                    resourceId,
                    payloadAfter: AdminAuditContext.Snapshot(payloadAfter)),
                ct);
        }
        catch (Exception ex)
        {
            // Audit não pode bloquear o fluxo — só loga.
            _logger.LogWarning(ex, "[AgentSandbox] Falha ao registrar audit '{Action}' pra '{ResourceId}'.",
                action, resourceId);
        }
    }
}
