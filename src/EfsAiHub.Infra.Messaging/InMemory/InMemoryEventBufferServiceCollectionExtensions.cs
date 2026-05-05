using EfsAiHub.Core.Abstractions.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Infra.Messaging.InMemory;

public static class InMemoryEventBufferServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IEventBuffer"/> com a implementação in-memory + cleaner.
    /// Para migrar pra Redis Streams: trocar esta chamada por
    /// <c>AddRedisStreamEventBuffer(...)</c> sem mexer em consumers.
    /// </summary>
    public static IServiceCollection AddInMemoryEventBuffer(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "EventBuffer:InMemory")
    {
        var section = configuration.GetSection(sectionName);
        services.Configure<InMemoryEventBufferOptions>(opts =>
        {
            var maxLen = section["MaxLengthPerStream"];
            var retention = section["RetentionAfterTerminalMinutes"];
            var idle = section["IdleEvictionAfterMinutes"];
            var cleanup = section["CleanupIntervalSeconds"];

            if (int.TryParse(maxLen, out var v1)) opts.MaxLengthPerStream = v1;
            if (int.TryParse(retention, out var v2)) opts.RetentionAfterTerminalMinutes = v2;
            if (int.TryParse(idle, out var v3)) opts.IdleEvictionAfterMinutes = v3;
            if (int.TryParse(cleanup, out var v4)) opts.CleanupIntervalSeconds = v4;
        });
        services.AddSingleton<InMemoryEventBuffer>();
        services.AddSingleton<IEventBuffer>(sp => sp.GetRequiredService<InMemoryEventBuffer>());
        services.AddHostedService<InMemoryEventBufferCleaner>();
        return services;
    }
}
