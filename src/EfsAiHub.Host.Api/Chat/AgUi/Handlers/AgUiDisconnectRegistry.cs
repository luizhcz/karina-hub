using System.Collections.Concurrent;

namespace EfsAiHub.Host.Api.Chat.AgUi.Handlers;

/// <summary>
/// Singleton que rastreia timers de grace period por executionId.
///
/// Quando o SSE desconecta sem RUN_FINISHED, um timer é agendado.
/// Se o usuário reconectar antes do timer expirar, o timer é cancelado.
/// Se o timer expirar sem reconexão, o workflow é cancelado via AgUiCancellationHandler.
///
/// Nunca aplica grace period a execuções com HITL pendente — o timeout do HITL governa.
/// </summary>
public sealed class AgUiDisconnectRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    /// <summary>
    /// Agenda o cancelamento do workflow após o grace period.
    /// Se já houver um timer para o mesmo executionId, substitui.
    /// </summary>
    public void Schedule(
        string executionId,
        TimeSpan gracePeriod,
        Func<Task> cancelAction,
        ILogger logger)
    {
        // Substitui timer existente (ex: reconexão rápida seguida de novo disconnect)
        if (_timers.TryRemove(executionId, out var old))
            old.Cancel();

        var cts = new CancellationTokenSource();
        _timers[executionId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(gracePeriod, cts.Token);
                _timers.TryRemove(executionId, out _);
                await cancelAction();
                logger.LogInformation(
                    "[Disconnect] Grace period expirado para '{ExecutionId}' — workflow cancelado por inatividade.",
                    executionId);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug(
                    "[Disconnect] Grace period cancelado para '{ExecutionId}' (reconexão detectada).",
                    executionId);
            }
        });
    }

    /// <summary>Cancela o grace period pendente (chamado na reconexão).</summary>
    public void Cancel(string executionId)
    {
        if (_timers.TryRemove(executionId, out var cts))
            cts.Cancel();
    }

    public bool HasPending(string executionId) => _timers.ContainsKey(executionId);
}
