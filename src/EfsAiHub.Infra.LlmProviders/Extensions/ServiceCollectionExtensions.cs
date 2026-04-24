using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using EfsAiHub.Infra.LlmProviders.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Infra.LlmProviders.Extensions;

/// <summary>
/// DI consolidado dos providers LLM (OpenAI, AzureOpenAI, AzureFoundry).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlmProviders(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AzureAIOptions>(config.GetSection(AzureAIOptions.SectionName));
        services.Configure<OpenAIOptions>(config.GetSection(OpenAIOptions.SectionName));

        services.AddSingleton<ILlmClientProvider, AzureFoundryClientProvider>();
        services.AddSingleton<ILlmClientProvider, AzureOpenAiClientProvider>();
        services.AddSingleton<ILlmClientProvider, OpenAiClientProvider>();

        return services;
    }

    /// <summary>
    /// Registra infraestrutura de resolução de <see cref="UserPersona"/>:
    /// <see cref="HttpPersonaProvider"/> como provider base (contrato NUNCA lança)
    /// e <see cref="PersonaContextAccessor"/> scoped para acesso durante request.
    ///
    /// O decorator de cache (<c>CachedPersonaProvider</c>) é registrado em
    /// Platform.Runtime porque depende de <c>IEfsRedisCache</c> (Infra.Persistence).
    /// Esta extension não o registra — fica em <c>AddRuntimeCaches()</c>.
    /// </summary>
    public static IServiceCollection AddPersonaResolution(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PersonaApiOptions>(config.GetSection(PersonaApiOptions.SectionName));
        services.AddHttpClient(nameof(HttpPersonaProvider));
        services.AddSingleton<HttpPersonaProvider>();
        services.AddScoped<IPersonaContextAccessor, PersonaContextAccessor>();
        return services;
    }
}
