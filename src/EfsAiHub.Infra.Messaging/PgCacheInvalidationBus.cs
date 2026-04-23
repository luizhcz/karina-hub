using System.Text.Json;
using EfsAiHub.Core.Abstractions.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EfsAiHub.Infra.Messaging;

/// <summary>
/// Implementação default do <see cref="ICacheInvalidationBus"/> usando
/// <c>pg_notify</c> no canal <see cref="PgNotifyDispatcher.CacheInvalidateChannel"/>.
///
/// <para>Arquitetura:</para>
/// <list type="bullet">
///   <item>Publish: <c>SELECT pg_notify('efs_cache_invalidate', json_payload)</c>
///   via conn curta do pool "general". Best-effort — falha é logada.</item>
///   <item>Subscribe: delega pro <see cref="PgNotifyDispatcher"/> singleton
///   que já mantém LISTEN permanente. Filtragem por <c>cacheName</c> acontece lá.</item>
///   <item>Echo filtering: publisher grava <see cref="SourcePodId"/> no payload;
///   subscriber ignora eventos do próprio pod comparando com o seu SourcePodId.</item>
/// </list>
///
/// <para>SourcePodId: hostname do container (gerado 1x no constructor). Dois
/// pods do mesmo deploy Kubernetes têm hostnames distintos — identificador
/// barato e confiável.</para>
/// </summary>
public sealed class PgCacheInvalidationBus : ICacheInvalidationBus
{
    private readonly NpgsqlDataSource _generalDataSource;
    private readonly PgNotifyDispatcher _dispatcher;
    private readonly ILogger<PgCacheInvalidationBus> _logger;

    public string SourcePodId { get; }

    public PgCacheInvalidationBus(
        [FromKeyedServices("general")] NpgsqlDataSource generalDataSource,
        PgNotifyDispatcher dispatcher,
        ILogger<PgCacheInvalidationBus> logger)
    {
        _generalDataSource = generalDataSource;
        _dispatcher = dispatcher;
        _logger = logger;
        SourcePodId = Environment.MachineName;
    }

    public async Task PublishInvalidateAsync(string cacheName, string key, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new PgNotifyDispatcher.CacheInvalidatePayload
        {
            CacheName = cacheName,
            Key = key,
            SourcePodId = SourcePodId,
        });

        try
        {
            await using var conn = await _generalDataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_notify(@channel, @payload)";
            cmd.Parameters.AddWithValue("channel", PgNotifyDispatcher.CacheInvalidateChannel);
            cmd.Parameters.AddWithValue("payload", payload);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Best-effort: TTL do L1 (≤60s) cobre perda transiente.
            _logger.LogWarning(ex,
                "[CacheInvalidationBus] pg_notify falhou para cache={Cache} key={Key}.",
                cacheName, key);
        }
    }

    public IDisposable Subscribe(string cacheName, Func<string, Task> handler)
    {
        // Filtragem de echo do próprio pod mora no bus — dispatcher é
        // agnóstico à semântica. Eventos com SourcePodId == SourcePodId
        // deste pod são ignorados (já limpamos o L1 localmente no próprio
        // Publish).
        return _dispatcher.SubscribeCacheInvalidate(cacheName, async (key, sourcePodId) =>
        {
            if (string.Equals(sourcePodId, SourcePodId, StringComparison.Ordinal))
                return;
            await handler(key);
        });
    }
}
