using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Core.Orchestration.Coordination;
using EfsAiHub.Infra.Messaging.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EfsAiHub.Infra.Messaging.Extensions;

/// <summary>
/// Composition root do Infra.Messaging. Registra os barramentos Postgres
/// (LISTEN/NOTIFY) usados pelo SSE e pela coordenação cross-pod. NpgsqlDataSource
/// com chaves "general" e "sse" deve ser registrado previamente pelo Infra.Persistence.
///
/// <see cref="PgNotifyDispatcher"/> é singleton hospedado: abre uma única conn PG
/// persistente no startup (canal global wf_events) e demultiplexa para subscribers
/// in-memory. Registrado antes de <see cref="PgEventBus"/>, que injeta o dispatcher.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddSingleton<PgNotifyDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<PgNotifyDispatcher>());
        services.AddSingleton<IWorkflowEventBus, PgEventBus>();
        services.AddSingleton<ICrossNodeBus, PgCrossNodeBus>();
        services.AddSingleton<ICacheInvalidationBus, PgCacheInvalidationBus>();
        return services;
    }

    /// <summary>
    /// Registra o <see cref="IEventBuffer"/> in-memory (1 pod) + cleaner background.
    /// Migração pra N pods: trocar esta chamada por AddRedisStreamEventBuffer(...)
    /// — mantém o mesmo IEventBuffer pros consumers (controllers/services).
    /// </summary>
    public static IServiceCollection AddEventBuffer(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddInMemoryEventBuffer(configuration);
    }
}
