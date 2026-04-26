using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Infra.Messaging;

/// <summary>
/// IWorkflowEventBus implementado com PostgreSQL LISTEN/NOTIFY (entrega em tempo real)
/// + tabela workflow_event_audit (persistência/replay).
///
/// Publish:
///   1. AppendAsync em workflow_event_audit (apenas eventos não-token) → retorna SequenceId
///   2. pg_notify no canal GLOBAL wf_events com envelope JSON (inclui ExecutionId)
///      A escolha de canal global permite multiplexar todos os subscribers em UMA
///      única conn LISTEN via <see cref="PgNotifyDispatcher"/> (singleton).
///
/// Subscribe:
///   1. Registra um <see cref="PgNotifyDispatcher.Subscription"/> para a execution
///   2. GetAllAsync → replay do histórico + constrói HashSet de dedup por SequenceId
///   3. Drena o ChannelReader do dispatcher (eventos live); descarta já vistos via replay
///
/// Capacidade: subscribers agora não consomem conns PG — limitados apenas pela
/// memória do processo (ordem de milhares). Substitui o padrão anterior (1 conn PG
/// dedicada por subscriber) que tinha teto em ~50 subscribers concorrentes.
/// </summary>
public sealed class PgEventBus : IWorkflowEventBus
{
    private readonly NpgsqlDataSource _generalDataSource;
    private readonly PgNotifyDispatcher _dispatcher;
    private readonly IWorkflowEventRepository _events;
    private readonly ILogger<PgEventBus> _logger;

    public PgEventBus(
        [FromKeyedServices("general")] NpgsqlDataSource generalDataSource,
        PgNotifyDispatcher dispatcher,
        IWorkflowEventRepository events,
        ILogger<PgEventBus> logger)
    {
        _generalDataSource = generalDataSource;
        _dispatcher = dispatcher;
        _events = events;
        _logger = logger;
    }

    public async Task PublishAsync(
        string executionId,
        WorkflowEventEnvelope envelope,
        CancellationToken ct = default)
    {
        // Outbox: non-token events persistem no DB e NOTIFY envia apenas referência
        // (sem Payload). O subscriber resolve o Payload completo via SequenceId.
        // Token events são entregues com payload completo via NOTIFY (não persistidos).
        string notifyPayload;

        if (envelope.EventType == "token")
        {
            notifyPayload = JsonSerializer.Serialize(envelope, JsonDefaults.Domain);
        }
        else
        {
            var sequenceId = await _events.AppendAsync(envelope, ct);
            notifyPayload = JsonSerializer.Serialize(new WorkflowEventEnvelope
            {
                EventType = envelope.EventType,
                ExecutionId = envelope.ExecutionId,
                SequenceId = sequenceId,
                Timestamp = envelope.Timestamp,
                Payload = string.Empty
            }, JsonDefaults.Domain);
        }

        // Conexão do pool "general" — curta duração para NOTIFY.
        // Canal global wf_events é consumido pelo PgNotifyDispatcher (singleton).
        await using var conn = await _generalDataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_notify(@channel, @payload)";
        cmd.Parameters.AddWithValue("channel", PgNotifyDispatcher.ChannelName);
        cmd.Parameters.AddWithValue("payload", notifyPayload);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async IAsyncEnumerable<WorkflowEventEnvelope> SubscribeAsync(
        string executionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var subscribeActivity = ActivitySources.EventBusSource.StartActivity(
            "eventbus.subscribe", ActivityKind.Internal);
        subscribeActivity?.SetTag("execution_id", executionId);

        MetricsRegistry.EventBusActiveSubscriptions.Add(1);

        // Registra no dispatcher ANTES do replay — elimina race: qualquer evento
        // publicado entre o replay e o início do drain já estará no channel
        // (dedup por SequenceId remove duplicatas).
        await using var subscription = _dispatcher.Subscribe(executionId);
        try
        {
            // 1. Replay do histórico
            var seen = new HashSet<long>();
            bool terminated = false;

            IReadOnlyList<WorkflowEventEnvelope> history;
            using (var replayActivity = ActivitySources.EventBusSource.StartActivity(
                "eventbus.subscribe.replay", ActivityKind.Internal))
            {
                try
                {
                    history = await _events.GetAllAsync(executionId, ct);
                    replayActivity?.SetTag("events.count", history.Count);
                }
                catch (Exception ex)
                {
                    MetricsRegistry.EventBusSubscribeSetupErrors.Add(1,
                        new KeyValuePair<string, object?>("phase", "replay"));
                    replayActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            }

            foreach (var evt in history)
            {
                if (evt.SequenceId > 0)
                    seen.Add(evt.SequenceId);

                yield return evt;

                if (evt.EventType is "workflow_completed" or "error")
                {
                    terminated = true;
                    break;
                }
            }

            if (terminated) yield break;

            // 2. Drena eventos live do dispatcher (filtrados por executionId pelo próprio dispatcher)
            await foreach (var evt in subscription.Reader.ReadAllAsync(ct))
            {
                // Dedup por SequenceId: pula events non-token já entregues via replay
                if (evt.EventType != "token" && evt.SequenceId > 0 && seen.Contains(evt.SequenceId))
                    continue;

                // Resolve referência: non-token events chegam sem Payload via NOTIFY (outbox)
                var resolved = evt;
                if (evt.EventType != "token" && evt.SequenceId > 0 && string.IsNullOrEmpty(evt.Payload))
                {
                    var full = await _events.GetBySequenceIdAsync(evt.SequenceId, ct);
                    if (full is not null)
                        resolved = full;
                    else
                        continue;
                }

                if (resolved.SequenceId > 0)
                    seen.Add(resolved.SequenceId);

                yield return resolved;

                if (resolved.EventType is "workflow_completed" or "error")
                    yield break;
            }
        }
        finally
        {
            MetricsRegistry.EventBusActiveSubscriptions.Add(-1);
            // Subscription descarta automaticamente via `await using` — remove writer do
            // dispatcher e completa o channel.
        }
    }

    public Task<IReadOnlyList<WorkflowEventEnvelope>> GetHistoryAsync(
        string executionId,
        CancellationToken ct = default)
        => _events.GetAllAsync(executionId, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<WorkflowEventEnvelope>>> GetHistoryBatchAsync(
        IEnumerable<string> executionIds,
        CancellationToken ct = default)
        => _events.GetAllByExecutionIdsAsync(executionIds, ct);
}
