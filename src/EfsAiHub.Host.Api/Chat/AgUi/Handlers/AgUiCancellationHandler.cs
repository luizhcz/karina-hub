using EfsAiHub.Core.Orchestration.Coordination;
using EfsAiHub.Core.Abstractions.Execution;

namespace EfsAiHub.Host.Api.Chat.AgUi.Handlers;

/// <summary>
/// Cancela um run em andamento. O cancelamento propaga para:
/// 1. CancellationToken local (se no mesmo pod)
/// 2. ICrossNodeBus (se em outro pod)
/// 3. O stream SSE emite RUN_ERROR com code CANCELLED
/// </summary>
public sealed class AgUiCancellationHandler
{
    private readonly IExecutionSlotRegistry _slots;
    private readonly ICrossNodeBus? _crossNodeBus;

    public AgUiCancellationHandler(
        IExecutionSlotRegistry slots,
        ICrossNodeBus? crossNodeBus = null)
    {
        _slots = slots;
        _crossNodeBus = crossNodeBus;
    }

    public async Task CancelAsync(string executionId, CancellationToken ct = default)
    {
        // Tenta cancelamento local
        var cancelled = _slots.TryCancel(executionId);

        if (!cancelled && _crossNodeBus is not null)
        {
            // Se não encontrou local, propaga cross-pod
            await _crossNodeBus.PublishCancelAsync(executionId, ct);
        }
    }
}
