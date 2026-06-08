namespace EfsAiHub.Core.Abstractions.BackgroundServices;

/// <summary>
/// Snapshot do estado em runtime de um hosted service. Populado pelo
/// <see cref="IBackgroundServiceHeartbeatSink"/> a cada tick (sucesso ou erro)
/// e consumido pelo endpoint admin /background-services.
///
/// Per-pod: o sink é singleton em memória. Em deploy multi-instance cada pod
/// tem o próprio dicionário de heartbeats. Pra view fleet-wide, agregar do
/// lado do consumer (out of scope deste tipo).
/// </summary>
public sealed record BackgroundServiceHeartbeat
{
    public required string Name { get; init; }

    /// <summary>Momento em que o hosted service entrou em ExecuteAsync no pod atual.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>Último tick — sucesso OU erro. Usado pra detectar "stale" comparando com Interval.</summary>
    public DateTimeOffset? LastTickAtUtc { get; init; }

    /// <summary>Último tick bem-sucedido. Diverge de LastTickAtUtc quando o serviço só erra.</summary>
    public DateTimeOffset? LastSuccessAtUtc { get; init; }

    /// <summary>Último tick com falha. Pareado com LastErrorMessage.</summary>
    public DateTimeOffset? LastErrorAtUtc { get; init; }

    /// <summary>Mensagem da última exceção (sem stack — UI mostra resumo).</summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>Contador total de ticks no pod atual (sucesso + erro).</summary>
    public long TickCount { get; init; }

    /// <summary>Contador total de erros no pod atual.</summary>
    public long ErrorCount { get; init; }
}
