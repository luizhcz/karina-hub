using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Encapsula o estado de rastreamento por nó dentro de uma execução:
/// nodeState (ConcurrentDictionary), agentSpans e o agente ativo atual.
/// Instanciado uma vez por execução — não registrado no DI container.
/// </summary>
internal sealed class NodeStateTracker : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NodeExecutionRecord> _nodeState =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StringBuilder> _outputBuffers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Activity?> _agentSpans = new();

    public string? CurrentAgentId { get; set; }

    // ── Acesso ao estado ────────────────────────────────────────────────────

    public NodeExecutionRecord? GetRecord(string nodeId) =>
        _nodeState.TryGetValue(nodeId, out var r) ? r : null;

    public IEnumerable<NodeExecutionRecord> AllRecords => _nodeState.Values;

    public bool TryGetRecord(string nodeId, out NodeExecutionRecord record) =>
        _nodeState.TryGetValue(nodeId, out record!);

    // ── Mutações ────────────────────────────────────────────────────────────

    public void SetRecord(string nodeId, NodeExecutionRecord record) =>
        _nodeState[nodeId] = record;

    /// <summary>
    /// Append streaming de token ao buffer do nó. O(1) amortizado no loop de tokens
    /// (elimina O(N²) do antigo string concat).
    /// </summary>
    public void AppendOutput(string nodeId, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sb = _outputBuffers.GetOrAdd(nodeId, _ => new StringBuilder());
        lock (sb) { sb.Append(text); }
    }

    /// <summary>
    /// Materializa o buffer do nó em <see cref="NodeExecutionRecord.Output"/>.
    /// Deve ser chamado imediatamente antes de persistir o record (end-of-agent / handoff).
    /// </summary>
    public void MaterializeOutput(string nodeId)
    {
        if (!_nodeState.TryGetValue(nodeId, out var rec)) return;
        if (_outputBuffers.TryGetValue(nodeId, out var sb))
        {
            lock (sb) { rec.Output = sb.ToString(); }
        }
    }

    /// <summary>Materializa todos os buffers pendentes (usado no finalize).</summary>
    public void MaterializeAllOutputs()
    {
        foreach (var kv in _outputBuffers)
        {
            if (_nodeState.TryGetValue(kv.Key, out var rec))
            {
                lock (kv.Value) { rec.Output = kv.Value.ToString(); }
            }
        }
    }

    public Activity? StartAgentSpan(string agentId, string? agentName, string workflowId, string executionId)
    {
        var span = ActivitySources.AgentInvocationSource.StartActivity("AgentInvocation");
        span?.SetTag("agent.id", agentId);
        span?.SetTag("agent.name", agentName ?? agentId);
        span?.SetTag("workflow.id", workflowId);
        span?.SetTag("execution.id", executionId);
        _agentSpans[agentId] = span;
        return span;
    }

    public bool TryEndAgentSpan(string agentId, out Activity? span)
    {
        if (!_agentSpans.TryGetValue(agentId, out span))
            return false;
        span?.Stop();
        span?.Dispose();
        _agentSpans.Remove(agentId);
        return true;
    }

    // ── IAsyncDisposable — finaliza spans órfãos ────────────────────────────

    public ValueTask DisposeAsync()
    {
        foreach (var span in _agentSpans.Values)
        {
            span?.Stop();
            span?.Dispose();
        }
        _agentSpans.Clear();
        return ValueTask.CompletedTask;
    }
}
