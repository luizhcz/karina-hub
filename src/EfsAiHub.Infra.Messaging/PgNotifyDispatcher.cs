using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Infra.Messaging;

/// <summary>
/// Fase 3 — dispatcher singleton que multiplexa LISTEN em uma única conexão PG
/// persistente. Demultiplexa NOTIFY pro canal "wf_events" por ExecutionId do payload.
///
/// Substitui o padrão anterior (1 conn PG dedicada por subscriber SSE) que tinha
/// teto estrutural em ~50 subscribers concorrentes (limite do pool "sse"). Com o
/// dispatcher, capacidade passa a ser limitada por memória do processo (milhares
/// de ChannelReader).
///
/// Reconexão automática com backoff exponencial se a conn cair. Durante a janela
/// de reconexão, novos NOTIFY podem ser perdidos — mas o SubscribeAsync em PgEventBus
/// cobre essa lacuna via replay do histórico em workflow_event_audit + dedup por
/// SequenceId.
///
/// Segurança concorrente: ConcurrentDictionary + lock por lista. NOTIFY handler roda
/// no thread do Npgsql; Subscribe/Unsubscribe podem vir de qualquer thread.
/// </summary>
public sealed class PgNotifyDispatcher : IHostedService, IAsyncDisposable
{
    /// <summary>Canal global onde PgEventBus.PublishAsync emite NOTIFY.</summary>
    public const string ChannelName = "wf_events";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgNotifyDispatcher> _logger;

    // executionId → lista de writers que devem receber eventos dessa execution.
    // Múltiplos subscribers da mesma execution (ex: duas abas do frontend) é caso de uso real.
    private readonly ConcurrentDictionary<string, List<ChannelWriter<WorkflowEventEnvelope>>> _subscribers = new();

    private NpgsqlConnection? _conn;
    private Task? _listenLoop;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _connMutex = new(1, 1);
    private int _stopped; // 0 = running, 1 = stopped (Interlocked)

    public PgNotifyDispatcher(
        [FromKeyedServices("sse")] NpgsqlDataSource dataSource,
        ILogger<PgNotifyDispatcher> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    // ── Subscribe / Unsubscribe ────────────────────────────────────────────────

    /// <summary>
    /// Registra um novo subscriber para a execution especificada. Retorna o par
    /// (reader, handle). O handle deve ser descartado (ou chamado <see cref="Unsubscribe"/>)
    /// ao final do consumo — senão o Channel nunca é completado e o subscriber vaza.
    /// </summary>
    public Subscription Subscribe(string executionId)
    {
        var channel = Channel.CreateUnbounded<WorkflowEventEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false, // dispatcher pode ser chamado de múltiplos NOTIFY em paralelo
        });

        var writer = channel.Writer;
        _subscribers.AddOrUpdate(
            executionId,
            _ => new List<ChannelWriter<WorkflowEventEnvelope>> { writer },
            (_, list) =>
            {
                lock (list) list.Add(writer);
                return list;
            });

        return new Subscription(this, executionId, channel.Reader, writer);
    }

    internal void Unsubscribe(string executionId, ChannelWriter<WorkflowEventEnvelope> writer)
    {
        if (_subscribers.TryGetValue(executionId, out var list))
        {
            lock (list)
            {
                list.Remove(writer);
                if (list.Count == 0)
                    _subscribers.TryRemove(executionId, out _);
            }
        }
        writer.TryComplete();
    }

    /// <summary>
    /// Handle de subscription que o consumidor descarta ao final. Garante que o writer
    /// é removido do dispatcher e o channel é completado, evitando leak.
    /// </summary>
    public sealed class Subscription : IAsyncDisposable
    {
        private readonly PgNotifyDispatcher _dispatcher;
        private readonly string _executionId;
        private readonly ChannelWriter<WorkflowEventEnvelope> _writer;
        private bool _disposed;

        public ChannelReader<WorkflowEventEnvelope> Reader { get; }

        internal Subscription(
            PgNotifyDispatcher dispatcher,
            string executionId,
            ChannelReader<WorkflowEventEnvelope> reader,
            ChannelWriter<WorkflowEventEnvelope> writer)
        {
            _dispatcher = dispatcher;
            _executionId = executionId;
            Reader = reader;
            _writer = writer;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _dispatcher.Unsubscribe(_executionId, _writer);
            return ValueTask.CompletedTask;
        }
    }

    // ── IHostedService ─────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await OpenAndListenAsync(_cts.Token);
        _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token));
        _logger.LogInformation("[PgNotifyDispatcher] Iniciado em canal '{Channel}'", ChannelName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Idempotente: o host chama StopAsync (IHostedService) e depois DisposeAsync
        // encadeia StopAsync novamente. Sem essa guarda, o segundo call tocaria _cts
        // já disposado.
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
        _logger.LogInformation("[PgNotifyDispatcher] Desligando…");
        _cts?.Cancel();
        if (_listenLoop is not null)
        {
            try { await _listenLoop.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("[PgNotifyDispatcher] Loop de LISTEN não concluiu em 5s no shutdown.");
            }
        }

        // Completa todos os channels pendentes para desbloquear subscribers.
        foreach (var (_, list) in _subscribers)
            lock (list)
                foreach (var w in list)
                    w.TryComplete();
        _subscribers.Clear();

        await _connMutex.WaitAsync(cancellationToken);
        try
        {
            if (_conn is not null)
            {
                await _conn.DisposeAsync();
                _conn = null;
            }
        }
        finally
        {
            _connMutex.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _connMutex.Dispose();
    }

    // ── Internal: conn + loop ──────────────────────────────────────────────────

    private async Task OpenAndListenAsync(CancellationToken ct)
    {
        using var activity = ActivitySources.EventBusSource.StartActivity(
            "dispatcher.open_conn", ActivityKind.Internal);

        await _connMutex.WaitAsync(ct);
        try
        {
            if (_conn is not null)
            {
                try { await _conn.DisposeAsync(); } catch { /* best effort */ }
            }

            _conn = await _dataSource.OpenConnectionAsync(ct);
            _conn.Notification += OnNotification;

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"LISTEN {ChannelName}";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            _connMutex.Release();
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        // Backoff exponencial: 200ms → 400ms → ... → max 10s
        var backoffMs = 200;
        const int backoffMaxMs = 10_000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_conn is null)
                {
                    await OpenAndListenAsync(ct);
                    backoffMs = 200; // reset após sucesso
                }

                // WaitAsync bloqueia até NOTIFY ou erro de conexão.
                await _conn!.WaitAsync(ct);
                backoffMs = 200;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                MetricsRegistry.EventBusBackgroundTaskTimeouts.Add(1);
                _logger.LogWarning(ex,
                    "[PgNotifyDispatcher] Conexão LISTEN caiu. Reconectando em {Ms}ms.", backoffMs);

                try { await Task.Delay(backoffMs, ct); }
                catch (OperationCanceledException) { break; }

                // Descarta conn atual; próxima iteração tenta reabrir.
                await _connMutex.WaitAsync(ct);
                try
                {
                    if (_conn is not null)
                    {
                        try { await _conn.DisposeAsync(); } catch { /* best effort */ }
                        _conn = null;
                    }
                }
                finally { _connMutex.Release(); }

                backoffMs = Math.Min(backoffMs * 2, backoffMaxMs);
            }
        }
    }

    private void OnNotification(object? sender, NpgsqlNotificationEventArgs args)
    {
        // Handler roda na thread do Npgsql — evitar trabalho pesado.
        try
        {
            var env = JsonSerializer.Deserialize<WorkflowEventEnvelope>(args.Payload);
            if (env is null) return;

            if (!_subscribers.TryGetValue(env.ExecutionId, out var list)) return;

            lock (list)
            {
                foreach (var writer in list)
                    writer.TryWrite(env);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[PgNotifyDispatcher] Mensagem NOTIFY malformada ignorada.");
        }
    }
}
