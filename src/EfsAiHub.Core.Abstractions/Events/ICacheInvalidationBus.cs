namespace EfsAiHub.Core.Abstractions.Events;

/// <summary>
/// Canal cross-pod para invalidação coordenada de L1 caches in-memory.
/// Implementação default (Postgres LISTEN/NOTIFY) vive em
/// <c>EfsAiHub.Infra.Messaging</c>.
///
/// <para>Protocolo:</para>
/// <list type="bullet">
///   <item>Publisher emite <see cref="PublishInvalidateAsync"/> quando UPDATE/DELETE/UPSERT
///   acontecer no seu recurso — todos os pods recebem via subscriber.</item>
///   <item>Cada subscriber guarda o seu <c>sourcePodId</c>; mensagens do próprio
///   pod são ignoradas (evita loop e refetch inútil).</item>
///   <item>Perda de mensagem é aceitável — TTL do L1 (≤60s) é safety net.</item>
/// </list>
/// </summary>
public interface ICacheInvalidationBus
{
    /// <summary>ID único do pod corrente — permite filtrar echo do próprio pod.</summary>
    string SourcePodId { get; }

    /// <summary>
    /// Publica uma invalidação. <paramref name="cacheName"/> identifica o cache
    /// (ex: <c>"persona"</c>, <c>"persona-tpl"</c>, <c>"model-pricing"</c>).
    /// <paramref name="key"/> identifica a entry invalidada (ex: scope, userId).
    /// Operação best-effort — falha transiente é logada mas não propaga.
    /// </summary>
    Task PublishInvalidateAsync(string cacheName, string key, CancellationToken ct = default);

    /// <summary>
    /// Registra um handler pra receber invalidações de um <paramref name="cacheName"/>
    /// específico. O handler é chamado DO OUTRO POD — eventos do próprio pod já
    /// são filtrados pela infra (comparando <c>sourcePodId</c>).
    ///
    /// Retorna um handle disposable; ao descartar, o handler é removido.
    /// </summary>
    IDisposable Subscribe(string cacheName, Func<string, Task> handler);
}
