using System.Text.Json;
using EfsAiHub.Platform.Runtime.Interfaces;
using EfsAiHub.Core.Orchestration.Coordination;
using EfsAiHub.Infra.Observability;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Infra.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Fix #A1 — escuta os canais <c>efs_exec_cancel</c> e <c>efs_hitl_resolved</c> via
/// conexão LISTEN dedicada no pool "sse" e encaminha para os serviços locais.
///
/// Cancel: chama <see cref="IExecutionSlotRegistry.TryCancel"/>. Se o executionId não existe neste pod,
/// o resultado é silenciosamente no-op (outro pod terá o CTS).
///
/// HITL: chama <see cref="HumanInteractionService.Resolve"/>. Idempotente — o primeiro
/// pod que detém o TCS resolve; os demais recebem false e apenas limpam o cache local.
/// </summary>
public sealed class CrossNodeCoordinator : BackgroundService
{
    private readonly NpgsqlDataSource _sseDataSource;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CrossNodeCoordinator> _logger;

    public CrossNodeCoordinator(
        [FromKeyedServices("sse")] NpgsqlDataSource sseDataSource,
        IServiceScopeFactory scopeFactory,
        ILogger<CrossNodeCoordinator> logger)
    {
        _sseDataSource = sseDataSource;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[CrossNodeCoordinator] Iniciando LISTEN em {Cancel} / {Hitl}.",
            PgCrossNodeBus.CancelChannel, PgCrossNodeBus.HitlResolvedChannel);

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            NpgsqlConnection? conn = null;
            try
            {
                conn = await _sseDataSource.OpenConnectionAsync(stoppingToken);
                conn.Notification += OnNotification;

                await using (var listen1 = conn.CreateCommand())
                {
                    listen1.CommandText = $"LISTEN \"{PgCrossNodeBus.CancelChannel}\"; LISTEN \"{PgCrossNodeBus.HitlResolvedChannel}\";";
                    await listen1.ExecuteNonQueryAsync(stoppingToken);
                }

                attempt = 0; // Reset on successful connection

                while (!stoppingToken.IsCancellationRequested)
                    await conn.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                var backoff = Math.Min(30_000, 1000 * (1 << Math.Min(attempt, 5)));
                var jitter = Random.Shared.Next(0, 1000);
                _logger.LogWarning(ex, "[CrossNodeCoordinator] LISTEN desconectou, reconectando em {Ms}ms.", backoff + jitter);
                try { await Task.Delay(backoff + jitter, stoppingToken); }
                catch (OperationCanceledException) { break; }
                attempt++;
            }
            finally
            {
                if (conn is not null)
                {
                    conn.Notification -= OnNotification;
                    await conn.DisposeAsync();
                }
            }
        }
    }

    private void OnNotification(object _, NpgsqlNotificationEventArgs args)
    {
        // Handler síncrono — dispatcha para scope fora do thread do driver.
        _ = Task.Run(() => HandleAsync(args.Channel, args.Payload));
    }

    private async Task HandleAsync(string channel, string payload)
    {
        try
        {
            if (string.Equals(channel, PgCrossNodeBus.CancelChannel, StringComparison.Ordinal))
            {
                var msg = JsonSerializer.Deserialize<CancelPayload>(payload);
                if (msg is null || string.IsNullOrEmpty(msg.executionId)) return;

                using var scope = _scopeFactory.CreateScope();
                var chatRegistry = scope.ServiceProvider.GetRequiredService<IExecutionSlotRegistry>();
                var cancelled = chatRegistry.TryCancel(msg.executionId);
                if (cancelled)
                {
                    MetricsRegistry.CrossNodeCancelReceived.Add(1);
                    _logger.LogInformation(
                        "[CrossNodeCoordinator] Cancel cross-pod aplicado localmente para '{ExecutionId}'.",
                        msg.executionId);
                }
            }
            else if (string.Equals(channel, PgCrossNodeBus.HitlResolvedChannel, StringComparison.Ordinal))
            {
                var msg = JsonSerializer.Deserialize<HitlPayload>(payload);
                if (msg is null || string.IsNullOrEmpty(msg.interactionId)) return;

                using var scope = _scopeFactory.CreateScope();
                var hitl = scope.ServiceProvider.GetRequiredService<IHumanInteractionService>();
                // publishToCross=false para não criar loop de NOTIFY.
                // CAS no banco decide quem venceu: se outro pod já resolveu, ResolveAsync retorna false
                // e apenas limpa estado local desse pod.
                var resolvedLocally = await hitl.ResolveAsync(
                    msg.interactionId, msg.resolution ?? string.Empty, msg.approved, publishToCross: false);
                if (resolvedLocally)
                {
                    MetricsRegistry.CrossNodeHitlResolvedReceived.Add(1);
                    _logger.LogInformation(
                        "[CrossNodeCoordinator] HITL '{InteractionId}' resolvida via cross-pod.",
                        msg.interactionId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CrossNodeCoordinator] Falha ao processar notification {Channel}.", channel);
        }
    }

    // ReSharper disable InconsistentNaming
    private sealed record CancelPayload(string executionId);
    private sealed record HitlPayload(string interactionId, string? resolution, bool approved);
    // ReSharper restore InconsistentNaming
}
