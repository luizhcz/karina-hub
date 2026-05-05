using System.Text.Json;

namespace EfsAiHub.Host.Api.Endpoints.Polling;

/// <summary>
/// Long-poll sintético sobre fonte DB-backed: tenta read, se vazio espera N ms
/// e tenta de novo até waitFor expirar. Padrão A/C usam este helper porque a
/// fonte (workflow_event_audit, evaluation_results) não tem mecanismo de
/// notificação assíncrona — só polling.
/// </summary>
public static class DbPollingHelper
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Tenta <paramref name="readSince"/> uma vez. Se vazio e waitFor &gt; 0, faz polling
    /// interno até retornar non-empty ou expirar. Marca terminal via callback.
    /// </summary>
    public static async Task<EventPollingResponse> PollAsync(
        Func<long, int, CancellationToken, Task<List<EventPollingItem>>> readSince,
        Func<bool, Task<bool>> isTerminalAsync,
        long since,
        int limit,
        TimeSpan waitFor,
        CancellationToken ct)
    {
        var items = await readSince(since, limit, ct).ConfigureAwait(false);
        var terminal = await isTerminalAsync(items.Count > 0).ConfigureAwait(false);

        if (items.Count > 0 || terminal || waitFor <= TimeSpan.Zero)
        {
            return Build(items, since, terminal);
        }

        var deadline = DateTimeOffset.UtcNow + waitFor;
        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            items = await readSince(since, limit, ct).ConfigureAwait(false);
            terminal = await isTerminalAsync(items.Count > 0).ConfigureAwait(false);
            if (items.Count > 0 || terminal) break;
        }

        return Build(items, since, terminal);
    }

    private static EventPollingResponse Build(List<EventPollingItem> items, long since, bool terminal)
    {
        var nextSince = items.Count > 0 ? items[^1].Seq : since;
        return new EventPollingResponse(items, nextSince, terminal);
    }

    /// <summary>
    /// Helper para serializar payload arbitrário (string ou objeto) como JsonElement.
    /// Strings JSON são reparseadas; objetos viram JSON serializado e parseado.
    /// </summary>
    public static JsonElement ToJsonElement(object? payload)
    {
        if (payload is null) return JsonDocument.Parse("null").RootElement;
        if (payload is string s)
        {
            try { return JsonDocument.Parse(s).RootElement; }
            catch (JsonException)
            {
                return JsonDocument.Parse(JsonSerializer.Serialize(s)).RootElement;
            }
        }
        return JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;
    }
}
