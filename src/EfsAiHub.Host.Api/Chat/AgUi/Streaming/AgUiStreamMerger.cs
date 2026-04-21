using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Host.Api.Chat.AgUi.Streaming;

/// <summary>
/// Merge de dois streams de AG-UI events:
/// 1. PgEventBus (lifecycle, step, tool, HITL, state — persistidos)
/// 2. AgUiTokenChannel (tokens streaming — in-memory, não persistidos)
///
/// Ambos são consumidos em paralelo e emitidos em ordem de chegada.
/// </summary>
public static class AgUiStreamMerger
{
    public static async IAsyncEnumerable<AgUiEvent> MergeAsync(
        IAsyncEnumerable<AgUiEvent> eventBusStream,
        ChannelReader<AgUiEvent> tokenStream,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Funnels both sources into a single bounded channel.
        // DropOldest previne OOM se o consumer SSE ficar lento (rede ruim, backpressure).
        // Tokens intermediários perdidos são recuperáveis via output final persistido.
        var merged = Channel.CreateBounded<AgUiEvent>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        // Producer 1: event bus events
        var t1 = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in eventBusStream.WithCancellation(ct))
                    await merged.Writer.WriteAsync(evt, ct);
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Signal that event bus is done — but only complete merged
                // when BOTH producers are done (handled below)
            }
        }, ct);

        // Producer 2: token channel events
        var t2 = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in tokenStream.ReadAllAsync(ct))
                    await merged.Writer.WriteAsync(evt, ct);
            }
            catch (OperationCanceledException) { }
        }, ct);

        // Complete merged channel when both producers finish
        _ = Task.Run(async () =>
        {
            try { await Task.WhenAll(t1, t2); }
            catch { /* swallow — ct cancelled */ }
            finally { merged.Writer.TryComplete(); }
        }, ct);

        // Consumer
        await foreach (var evt in merged.Reader.ReadAllAsync(ct))
            yield return evt;
    }
}
