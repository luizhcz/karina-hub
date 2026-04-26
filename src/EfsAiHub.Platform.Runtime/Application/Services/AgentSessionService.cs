using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Agents.AI;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Gerencia sessões de conversa multi-turn com agentes.
///
/// Fluxo por turn:
///   1. Carrega AgentSessionRecord do store (estado serializado)
///   2. Reconstrói o AIAgent via AgentFactory com a mesma AgentDefinition
///   3. Desserializa o AgentSession com agent.DeserializeSessionAsync()
///   4. Popula DelegateExecutor.Current.Value com ExecutionContext (ProjectId etc)
///      pra que middlewares per-projeto (BlocklistChatClient, TokenTracking) tenham contexto
///   5. Executa agent.RunAsync(message, session)
///   6. Re-serializa via agent.SerializeSession(session) e persiste
/// </summary>
public class AgentSessionService
{
    private readonly IAgentSessionStore _sessionStore;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentFactory _agentFactory;
    private readonly IProjectContextAccessor _projectContext;
    private readonly ILogger<AgentSessionService> _logger;

    public AgentSessionService(
        IAgentSessionStore sessionStore,
        IAgentDefinitionRepository agentRepo,
        IAgentFactory agentFactory,
        IProjectContextAccessor projectContext,
        ILogger<AgentSessionService> logger)
    {
        _sessionStore = sessionStore;
        _agentRepo = agentRepo;
        _agentFactory = agentFactory;
        _projectContext = projectContext;
        _logger = logger;
    }

    // ── Criação de sessão ────────────────────────────────────────────────────

    public async Task<AgentSessionRecord> CreateSessionAsync(
        string agentId,
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>? initialHistory = null,
        CancellationToken ct = default)
    {
        var definition = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new KeyNotFoundException($"Agente '{agentId}' não encontrado.");

        var agent = (AIAgent)(await _agentFactory.CreateAgentAsync(definition, ct)).Value;
        var session = await agent.CreateSessionAsync(ct);

        // Injeta histórico prévio na sessão usando a API oficial do framework
        if (initialHistory is { Count: > 0 })
        {
            session.SetInMemoryChatHistory(initialHistory.ToList());
            _logger.LogInformation(
                "Sessão para agente '{AgentId}': injetadas {Count} mensagens de histórico.",
                agentId, initialHistory.Count);
        }

        var serialized = await agent.SerializeSessionAsync(session, null, ct);

        var record = new AgentSessionRecord
        {
            SessionId = Guid.NewGuid().ToString(),
            AgentId = agentId,
            SerializedState = serialized,
            TurnCount = 0
        };

        await _sessionStore.CreateAsync(record, ct);
        _logger.LogInformation("Sessão '{SessionId}' criada para agente '{AgentId}'.", record.SessionId, agentId);
        return record;
    }

    // ── Execução de turn (non-streaming) ────────────────────────────────────

    public async Task<(string Response, AgentSessionRecord Session)> RunAsync(
        string sessionId,
        string message,
        CancellationToken ct = default)
    {
        var (agent, session, record) = await LoadSessionAsync(sessionId, ct);
        EnsureExecutionContext(record, message);

        _logger.LogInformation("Turn {Turn} na sessão '{SessionId}'.", record.TurnCount + 1, sessionId);

        var response = await agent.RunAsync(message, session, cancellationToken: ct);
        var responseText = response?.ToString() ?? string.Empty;

        await PersistSessionAsync(agent, session, record, ct);
        return (responseText, record);
    }

    // ── Execução de turn (streaming) ────────────────────────────────────────

    public async IAsyncEnumerable<string> RunStreamingAsync(
        string sessionId,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (agent, session, record) = await LoadSessionAsync(sessionId, ct);
        EnsureExecutionContext(record, message);

        _logger.LogInformation("Turn streaming {Turn} na sessão '{SessionId}'.", record.TurnCount + 1, sessionId);

        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: ct))
        {
            var text = update?.ToString();
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }

        await PersistSessionAsync(agent, session, record, ct);
    }

    // ── Leitura ──────────────────────────────────────────────────────────────

    public Task<AgentSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
        => _sessionStore.GetByIdAsync(sessionId, ct);

    public Task<IReadOnlyList<AgentSessionRecord>> ListByAgentAsync(string agentId, CancellationToken ct = default)
        => _sessionStore.GetByAgentIdAsync(agentId, ct);

    // ── Encerramento ─────────────────────────────────────────────────────────

    public async Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var deleted = await _sessionStore.DeleteAsync(sessionId, ct);
        if (!deleted)
            throw new KeyNotFoundException($"Sessão '{sessionId}' não encontrada.");

        _logger.LogInformation("Sessão '{SessionId}' encerrada.", sessionId);
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

    private async Task<(AIAgent Agent, AgentSession Session, AgentSessionRecord Record)> LoadSessionAsync(
        string sessionId,
        CancellationToken ct)
    {
        var record = await _sessionStore.GetByIdAsync(sessionId, ct)
            ?? throw new KeyNotFoundException($"Sessão '{sessionId}' não encontrada.");

        var definition = await _agentRepo.GetByIdAsync(record.AgentId, ct)
            ?? throw new InvalidOperationException(
                $"Agente '{record.AgentId}' da sessão '{sessionId}' não existe mais.");

        var agent = (AIAgent)(await _agentFactory.CreateAgentAsync(definition, ct)).Value;
        var session = await agent.DeserializeSessionAsync(record.SerializedState, null, ct);

        return (agent, session, record);
    }

    private async Task PersistSessionAsync(
        AIAgent agent,
        AgentSession session,
        AgentSessionRecord record,
        CancellationToken ct)
    {
        record.SerializedState = await agent.SerializeSessionAsync(session, null, ct);
        record.TurnCount++;
        record.LastAccessedAt = DateTime.UtcNow;
        await _sessionStore.UpdateAsync(record, ct);
    }

    /// <summary>
    /// Standalone agent sessions não passam pelo WorkflowRunnerService — então não há
    /// ExecutionContext criado upstream. Middlewares per-projeto (BlocklistChatClient)
    /// leem ProjectId via DelegateExecutor.Current.Value e fail-secure se ausente.
    /// Aqui preenchemos o mínimo necessário pro pipeline rodar com contexto correto.
    /// <para>
    /// Visibilidade <c>internal</c> (não <c>private</c>) pra permitir testes diretos sem
    /// mock pesado de IAgentSessionStore + IAgentDefinitionRepository + IAgentFactory.
    /// Platform.Runtime tem <c>InternalsVisibleTo("EfsAiHub.Tests.Unit")</c>.
    /// </para>
    /// </summary>
    internal void EnsureExecutionContext(AgentSessionRecord record, string message)
    {
        var ctx = _projectContext.Current;

        // Fail-secure: ProjectContext.IsExplicit=false significa que ninguém populou o
        // AsyncLocal — caminho não-HTTP detectado. Guardrails per-projeto não podem ser
        // aplicados corretamente nesse caso. Throw em vez de só warning evita config errada
        // ser aplicada silenciosamente. (HTTP fallback "default" tem IsExplicit=true e passa.)
        if (!ctx.IsExplicit)
        {
            _logger.LogError(
                "[AgentSession] Sessão '{SessionId}': ProjectContext não foi populado pelo ProjectMiddleware " +
                "(IsExplicit=false). Caminho não-HTTP — guardrails per-projeto não podem ser aplicados.",
                record.SessionId);
            throw new InvalidOperationException(
                "ProjectContext deve ser populado pelo ProjectMiddleware antes de iniciar AgentSession. " +
                "Verifique se o pipeline HTTP foi acionado ou popule o IProjectContextAccessor manualmente.");
        }

        var projectId = ctx.ProjectId;

        DelegateExecutor.Current.Value = new EfsAiHub.Core.Agents.Execution.ExecutionContext(
            ExecutionId: record.SessionId,
            WorkflowId: $"agent-session:{record.AgentId}",
            Input: message,
            PromptVersions: new ConcurrentDictionary<string, string>(),
            NodeCallback: null,
            // Standalone session não tem cap de tokens configurado — sem enforcement (0 = off).
            Budget: new EfsAiHub.Core.Agents.Execution.ExecutionBudget(maxTokensPerExecution: 0),
            UserId: null,
            GuardMode: EfsAiHub.Core.Agents.Execution.AccountGuardMode.None,
            AgentVersions: new ConcurrentDictionary<string, string>(),
            ProjectId: projectId);
    }
}
