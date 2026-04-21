using System.Collections.Concurrent;
using EfsAiHub.Core.Agents.Signals;
using EfsAiHub.Core.Agents.Enrichment;
using EfsAiHub.Core.Agents.Knowledge;
using EfsAiHub.Core.Abstractions.Execution;

namespace EfsAiHub.Core.Agents.Execution;

/// <summary>
/// Contexto de execução propagado via AsyncLocal para toda a árvore async de um workflow.
/// Substitui os 5 campos AsyncLocal independentes anteriores, reduzindo o número de slots
/// na thread e centralizando o ciclo de vida (set/clear em WorkflowRunnerService).
/// </summary>
public sealed record ExecutionContext(
    string ExecutionId,
    string WorkflowId,
    string? Input,
    ConcurrentDictionary<string, string> PromptVersions,
    Action<string, bool, string>? NodeCallback,
    ExecutionBudget Budget,
    string? UserId = null,
    AccountGuardMode GuardMode = AccountGuardMode.None,
    // Fase 1 — rastreia qual snapshot de agente está rodando.
    // Preenchido pelo AgentFactory quando resolve AgentVersion; null em execuções legadas.
    ConcurrentDictionary<string, string>? AgentVersions = null,
    // Fase 3 — seam para RAG (Fase 4): documentos recuperados a serem injetados no prompt.
    // Null até um IKnowledgeRetriever ser registrado. Middleware de augmentation consumirá daqui.
    IReadOnlyList<RetrievedDocument>? RetrievedDocuments = null,
    // Fase 7 — bag de sinais de escalação emitidos por agentes via EscalationSignalFunction.
    // O IEscalationRouter consome este bag para decidir o próximo nó com base nas RoutingRules do workflow.
    ConcurrentQueue<AgentEscalationSignal>? EscalationSignals = null,
    // Sprint 5 — modo de execução (Production/Sandbox).
    // Cada camada checa este campo para ajustar comportamento (C6: sem filter centralizado).
    ExecutionMode Mode = ExecutionMode.Production,
    // Shared state — delegate para tools atualizarem o AG-UI shared state (agent drafts).
    // Setado pelo WorkflowRunnerService quando a execução é de Chat. Null para execuções não-chat.
    // Assinatura: (path, value) → Task. Ex: ("agents/coletor-boleta", jsonElement).
    Func<string, System.Text.Json.JsonElement, Task>? UpdateSharedState = null,
    // ConversationId (threadId) da execução — usado pela UpdateStateFunction para saber
    // qual conversa atualizar. Extraído de execution.Metadata["conversationId"].
    string? ConversationId = null,
    // Regras declarativas de enrichment do workflow — consumidas pelo GenericEnricher executor.
    // Populadas pelo WorkflowRunnerService a partir de WorkflowConfiguration.EnrichmentRules.
    IReadOnlyList<EnrichmentRule>? EnrichmentRules = null);

/// <summary>
/// Modo de proteção de conta aplicado a tool calls de uma execução:
///   None                — sem enforcement
///   ClientLocked        — tools sensíveis têm 'conta'/'account' sobrescritos pelo userId da sessão;
///                         SendOrder rejeita boletas com account divergente
///   AssessorLogOnly     — divergências são logadas como anomalia (assessor opera múltiplas contas)
/// </summary>
public enum AccountGuardMode
{
    None,
    ClientLocked,
    AssessorLogOnly
}

/// <summary>
/// Contador mutável de tokens LLM consumidos durante uma execução. Compartilhado via
/// <see cref="ExecutionContext"/> (AsyncLocal) com o TokenTrackingChatClient.
/// Thread-safe via Interlocked — múltiplas tools/agentes em paralelo incrementam o mesmo contador.
/// </summary>
public sealed class ExecutionBudget
{
    private long _totalTokens;
    // decimal não é suportado por Interlocked — lock dedicado barato por execução.
    private readonly object _costLock = new();
    private decimal _totalCostUsd;

    public int MaxTokensPerExecution { get; }

    /// <summary>Fase 2 — teto de custo em USD para a execução. null/≤0 = sem enforcement.</summary>
    public decimal? MaxCostUsd { get; }

    public long TotalTokens => Interlocked.Read(ref _totalTokens);

    public decimal TotalCostUsd
    {
        get { lock (_costLock) return _totalCostUsd; }
    }

    public ExecutionBudget(int maxTokensPerExecution, decimal? maxCostUsd = null)
    {
        MaxTokensPerExecution = maxTokensPerExecution;
        MaxCostUsd = maxCostUsd;
    }

    public void Add(long tokens) => Interlocked.Add(ref _totalTokens, tokens);

    /// <summary>Acumula custo em USD thread-safe. Retorna total atualizado.</summary>
    public decimal AddCost(decimal costUsd)
    {
        lock (_costLock)
        {
            _totalCostUsd += costUsd;
            return _totalCostUsd;
        }
    }

    public bool IsExceeded =>
        (MaxTokensPerExecution > 0 && Interlocked.Read(ref _totalTokens) >= MaxTokensPerExecution) ||
        IsCostExceeded;

    public bool IsCostExceeded
    {
        get
        {
            if (MaxCostUsd is null || MaxCostUsd.Value <= 0) return false;
            lock (_costLock) return _totalCostUsd >= MaxCostUsd.Value;
        }
    }
}
