using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Infra.Messaging;

/// Fase 3 — renomeado de PgEventBus → PgEventBus.

/// <summary>
/// IWorkflowEventBus implementado com PostgreSQL LISTEN/NOTIFY (entrega em tempo real)
/// + tabela workflow_event_audit (persistência/replay).
///
/// Publish:
///   1. AppendAsync em workflow_event_audit (apenas eventos não-token) → retorna SequenceId gerado
///   2. pg_notify wf_{executionId_hex} com o envelope serializado em JSON (inclui SequenceId)
///   Conexão obtida do pool do NpgsqlDataSource — elimina overhead de TCP handshake por chamada.
///
/// Subscribe:
///   1. LISTEN canal → buffer local (garante zero lacunas)
///   2. GetAllAsync → replay do histórico + construção do HashSet de dedup por SequenceId
///   3. Drain buffer/live → pula eventos não-token já cobertos pelo replay (dedup determinístico por SequenceId)
///   Dedup por SequenceId (long, chave primária da tabela) elimina risco de colisão da
///   implementação anterior que usava EventType+Timestamp.Ticks.
/// </summary>
public sealed class PgEventBus : IWorkflowEventBus
{
    private readonly NpgsqlDataSource _generalDataSource;
    private readonly NpgsqlDataSource _sseDataSource;
    private readonly IWorkflowEventRepository _events;
    private readonly ILogger<PgEventBus> _logger;

    public PgEventBus(
        [FromKeyedServices("general")] NpgsqlDataSource generalDataSource,
        [FromKeyedServices("sse")] NpgsqlDataSource sseDataSource,
        IWorkflowEventRepository events,
        ILogger<PgEventBus> logger)
    {
        _generalDataSource = generalDataSource;
        _sseDataSource = sseDataSource;
        _events = events;
        _logger = logger;
    }

    // ── PublishAsync ──────────────────────────────────────────────────────────

    public async Task PublishAsync(
        string executionId,
        WorkflowEventEnvelope envelope,
        CancellationToken ct = default)
    {
        // Outbox completo: non-token events são persistidos no DB e NOTIFY envia apenas referência
        // (sem Payload). O subscriber resolve o Payload completo do DB pelo SequenceId.
        // Token events são entregues com payload completo via NOTIFY (não persistidos, sempre pequenos).
        string notifyPayload;

        if (envelope.EventType == "token")
        {
            notifyPayload = JsonSerializer.Serialize(envelope);
        }
        else
        {
            var sequenceId = await _events.AppendAsync(envelope, ct);
            // Referência mínima — subscriber faz fetch pelo SequenceId
            notifyPayload = JsonSerializer.Serialize(new WorkflowEventEnvelope
            {
                EventType = envelope.EventType,
                ExecutionId = envelope.ExecutionId,
                SequenceId = sequenceId,
                Timestamp = envelope.Timestamp,
                Payload = string.Empty // Referência — subscriber resolve do DB
            });
        }

        // Conexão obtida do pool "general" — conexão curta para NOTIFY
        await using var conn = await _generalDataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_notify(@channel, @payload)";
        cmd.Parameters.AddWithValue("channel", ChannelFor(executionId));
        cmd.Parameters.AddWithValue("payload", notifyPayload);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── SubscribeAsync ────────────────────────────────────────────────────────

    public async IAsyncEnumerable<WorkflowEventEnvelope> SubscribeAsync(
        string executionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Buffer local: garante que notificações recebidas durante o replay não se percam
        var liveChannel = Channel.CreateUnbounded<WorkflowEventEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // CTS vinculado ao token externo; usado para encerrar o loop WaitAsync
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Conexão dedicada do pool "sse" para LISTEN — mantida aberta durante toda a subscrição.
        // Pool isolado evita que conexões SSE esgotem o pool "general" usado pelo Chat Path.
        // Ao ser descartada no finally, retorna ao pool e o Npgsql executa UNLISTEN * implicitamente.
        var conn = await _sseDataSource.OpenConnectionAsync(ct);
        try
        {
            // 1. Registrar handler ANTES de LISTEN para garantir zero lacunas
            conn.Notification += (_, args) =>
            {
                try
                {
                    var env = JsonSerializer.Deserialize<WorkflowEventEnvelope>(
                        args.Payload);
                    if (env is not null)
                        liveChannel.Writer.TryWrite(env);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[PgEventBus] Mensagem NOTIFY malformada ignorada.");
                }
            };

            // 2. LISTEN no canal desta execução
            await using (var listenCmd = conn.CreateCommand())
            {
                listenCmd.CommandText = $"LISTEN \"{ChannelFor(executionId)}\"";
                await listenCmd.ExecuteNonQueryAsync(ct);
            }

            // 3. Replay do histórico; constrói conjunto de dedup por SequenceId (determinístico)
            var seen = new HashSet<long>();
            bool terminated = false;

            var history = await _events.GetAllAsync(executionId, ct);
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

            if (terminated)
            {
                linkedCts.Cancel();
                liveChannel.Writer.TryComplete();
                yield break;
            }

            // 4. Loop background: chama WaitAsync para que Npgsql dispare conn.Notification
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!linkedCts.Token.IsCancellationRequested)
                        await conn.WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Erro no loop WaitAsync do LISTEN. ExecutionId={ExecutionId}", executionId);
                }
                finally
                {
                    liveChannel.Writer.TryComplete();
                }
            }, CancellationToken.None);

            // 5. Drena notificações bufferizadas e em tempo real
            await foreach (var evt in liveChannel.Reader.ReadAllAsync(ct))
            {
                // Dedup por SequenceId: pula eventos não-token já entregues via replay do histórico.
                if (evt.EventType != "token" && evt.SequenceId > 0 && seen.Contains(evt.SequenceId))
                    continue;

                // Resolver referência: non-token events chegam sem Payload via NOTIFY (outbox completo)
                var resolved = evt;
                if (evt.EventType != "token" && evt.SequenceId > 0 && string.IsNullOrEmpty(evt.Payload))
                {
                    var full = await _events.GetBySequenceIdAsync(evt.SequenceId, ct);
                    if (full is not null)
                        resolved = full;
                    else
                        continue; // Evento não encontrado no DB — race improvável mas defensivo
                }

                if (resolved.SequenceId > 0)
                    seen.Add(resolved.SequenceId);

                yield return resolved;

                if (resolved.EventType is "workflow_completed" or "error")
                {
                    linkedCts.Cancel();
                    liveChannel.Writer.TryComplete();
                    yield break;
                }
            }
        }
        finally
        {
            linkedCts.Cancel();
            await conn.DisposeAsync();
        }
    }

    // ── GetHistoryAsync ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<WorkflowEventEnvelope>> GetHistoryAsync(
        string executionId,
        CancellationToken ct = default)
        => _events.GetAllAsync(executionId, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<WorkflowEventEnvelope>>> GetHistoryBatchAsync(
        IEnumerable<string> executionIds,
        CancellationToken ct = default)
        => _events.GetAllByExecutionIdsAsync(executionIds, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Nome do canal NOTIFY/LISTEN. Formato: "wf_" + UUID sem hífens (35 chars, &lt; 63 bytes).
    /// </summary>
    private static string ChannelFor(string executionId)
        => $"wf_{executionId.Replace("-", "")}";
}
