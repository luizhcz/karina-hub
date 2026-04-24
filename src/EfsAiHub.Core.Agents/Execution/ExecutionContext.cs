using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Agents.Enrichment;
using EfsAiHub.Core.Agents.Knowledge;
using EfsAiHub.Core.Agents.Signals;

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
    // Rastreia qual snapshot de agente está rodando.
    // Preenchido pelo AgentFactory quando resolve AgentVersion; null em execuções legadas.
    ConcurrentDictionary<string, string>? AgentVersions = null,
    // Seam para RAG: documentos recuperados a serem injetados no prompt.
    // Null até um IKnowledgeRetriever ser registrado. Middleware de augmentation consumirá daqui.
    IReadOnlyList<RetrievedDocument>? RetrievedDocuments = null,
    // Bag de sinais de escalação emitidos por agentes via EscalationSignalFunction.
    // O IEscalationRouter consome este bag para decidir o próximo nó com base nas RoutingRules do workflow.
    ConcurrentQueue<AgentEscalationSignal>? EscalationSignals = null,
    // Modo de execução (Production/Sandbox).
    // Cada camada checa este campo para ajustar comportamento (sem filter centralizado).
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
    IReadOnlyList<EnrichmentRule>? EnrichmentRules = null,
    // Persona do usuário para personalização de prompts. Resolvida pelo
    // PersonaResolutionMiddleware (chat) ou WorkflowRunnerService (standalone).
    // Null = sem personalização (fluxo anterior à feature). AgentFactory/SystemMessageBuilder
    // lidam com null e caem no prompt base invariante.
    UserPersona? Persona = null,
    // ProjectId do caller. Propagado pro TokenTrackingChatClient gravar
    // em llm_token_usage + usado pelo PersonaPromptComposer pra montar scope
    // project-aware. Null preserva compat com execuções legadas.
    string? ProjectId = null,
    // Assignments de experiment A/B por agentId. Composer resolve o
    // experiment ativo pro (projectId, scope) do agent + faz bucketing
    // determinístico por userId, e grava aqui. TokenTrackingChatClient lê
    // pra persistir ExperimentId + Variant em llm_token_usage. Null quando
    // nenhum agent dessa execução participou de experiment.
    ConcurrentDictionary<string, ExperimentAssignment>? ExperimentAssignments = null);

/// <summary>
/// Modo de proteção de conta aplicado a tool calls de uma execução:
///   None                — sem enforcement
///   ClientLocked        — tools sensíveis têm 'conta'/'account' sobrescritos pelo userId da sessão;
///                         SendOrder rejeita boletas com account divergente
///   AdminLogOnly        — divergências são logadas como anomalia (admin opera múltiplas contas)
/// </summary>
public enum AccountGuardMode
{
    None,
    ClientLocked,
    AdminLogOnly
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

    /// <summary>Teto de custo em USD para a execução. null/≤0 = sem enforcement.</summary>
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
