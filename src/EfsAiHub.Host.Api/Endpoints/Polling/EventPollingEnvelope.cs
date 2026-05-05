using System.Text.Json;

namespace EfsAiHub.Host.Api.Endpoints.Polling;

/// <summary>
/// Shape unificado das respostas de polling fallback (alternativa a SSE).
/// Mesmo envelope nos 4 endpoints (executions / conversations / agent-sessions /
/// evaluation-runs) — clientes consomem via 1 SDK function só.
/// </summary>
public sealed record EventPollingResponse(
    IReadOnlyList<EventPollingItem> Events,
    long NextSince,
    bool Terminal);

public sealed record EventPollingItem(
    long Seq,
    string Type,
    JsonElement Payload,
    DateTimeOffset OccurredAt);

public static class EventPollingValidation
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public const int MaxWaitMs = 25_000;

    public static (long since, int limit, TimeSpan waitFor, string? error) Parse(long? since, int? limit, int? waitMs)
    {
        var s = since ?? 0;
        var l = limit ?? DefaultLimit;
        var w = waitMs ?? 0;

        if (s < 0) return (0, 0, TimeSpan.Zero, "since must be >= 0");
        if (l < 1 || l > MaxLimit) return (0, 0, TimeSpan.Zero, $"limit must be in [1, {MaxLimit}]");
        if (w < 0 || w > MaxWaitMs) return (0, 0, TimeSpan.Zero, $"waitMs must be in [0, {MaxWaitMs}]");

        return (s, l, TimeSpan.FromMilliseconds(w), null);
    }
}
