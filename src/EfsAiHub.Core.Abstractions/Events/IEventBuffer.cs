namespace EfsAiHub.Core.Abstractions.Events;

/// <summary>
/// Buffer de eventos cursor-based para fallback HTTP polling de streams SSE.
/// Suporta retomada idempotente via Seq monotônico por streamKey.
///
/// <para>
/// Implementação atual: <c>InMemoryEventBuffer</c> (1 pod). Migração para Redis
/// Streams é uma troca de DI binding — a interface foi modelada espelhando a
/// semântica de <c>XADD</c>/<c>XREAD COUNT|BLOCK</c>/<c>XADD MAXLEN</c>/<c>EXPIRE</c>:
/// <list type="bullet">
///   <item><c>AppendAsync</c> ↔ <c>XADD streamKey * ...</c> (ID auto, MAXLEN ~ N).</item>
///   <item><c>ReadSinceAsync</c> ↔ <c>XREAD [BLOCK ms] COUNT n STREAMS streamKey since</c>.</item>
///   <item><c>MarkTerminalAsync</c> ↔ <c>XADD __terminal__</c> + <c>EXPIRE streamKey ttl</c>.</item>
/// </list>
/// Controllers e services consomem só esta interface — não dependem da impl.
/// </para>
/// </summary>
public interface IEventBuffer
{
    /// <summary>
    /// Append do evento ao stream identificado por streamKey. Retorna o seq monotônico
    /// atribuído pela implementação. Lança <see cref="InvalidOperationException"/> se
    /// o stream já foi marcado como terminal.
    /// </summary>
    Task<long> AppendAsync(string streamKey, BufferEvent ev, CancellationToken ct = default);

    /// <summary>
    /// Lê eventos com Seq &gt; since, ordenados ASC, até limit registros. Se waitFor &gt; 0
    /// e não houver eventos novos no momento da chamada, aguarda (long-poll) até chegar
    /// o primeiro evento ou o timeout. Sempre retorna <see cref="BufferPage"/> — eventos
    /// vazios são indicados pela lista vazia, não exception.
    /// </summary>
    Task<BufferPage> ReadSinceAsync(string streamKey, long since, int limit, TimeSpan waitFor, CancellationToken ct = default);

    /// <summary>
    /// Marca o stream como concluído (Completed/Failed/Cancelled). Reads subsequentes
    /// retornam <see cref="BufferPage.Terminal"/> = true. AppendAsync rejeita após esse
    /// ponto. Inicia TTL de retenção configurada na implementação — depois disso o
    /// stream é colhido pela rotina de cleanup.
    /// </summary>
    Task MarkTerminalAsync(string streamKey, CancellationToken ct = default);
}

public sealed record BufferEvent(string Type, string PayloadJson, DateTimeOffset OccurredAt);

public sealed record BufferedRecord(long Seq, string Type, string PayloadJson, DateTimeOffset OccurredAt);

public sealed record BufferPage(IReadOnlyList<BufferedRecord> Events, long NextSince, bool Terminal);
