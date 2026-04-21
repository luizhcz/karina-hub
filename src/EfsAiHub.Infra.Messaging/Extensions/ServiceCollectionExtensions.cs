using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Core.Orchestration.Coordination;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Infra.Messaging.Extensions;

/// <summary>
/// Fase 3 — composition root do Infra.Messaging. Registra os barramentos Postgres
/// (LISTEN/NOTIFY) usados pelo SSE e pela coordenação cross-pod. NpgsqlDataSource
/// com chaves "general" e "sse" deve ser registrado previamente pelo Infra.Persistence.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowEventBus, PgEventBus>();
        services.AddSingleton<ICrossNodeBus, PgCrossNodeBus>();
        return services;
    }
}
