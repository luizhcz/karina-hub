using EfsAiHub.Infra.LlmProviders.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Infra.LlmProviders.Extensions;

/// <summary>
/// Phase 12 — DI consolidado dos providers LLM (OpenAI, AzureOpenAI, AzureFoundry).
/// Substitui as 5 chamadas manuais que viviam no <c>Program.cs</c> do Host.Api.
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
}
