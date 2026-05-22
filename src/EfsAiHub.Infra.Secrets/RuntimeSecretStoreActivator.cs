using Amazon.SecretsManager;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Secrets.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Secrets;

/// <summary>
/// Roda o <see cref="SecretsPreloader"/> e registra o <see cref="IRuntimeSecretStore"/>
/// resultante no DI. Deve ser invocado uma única vez, no boot, antes do app começar
/// a aceitar tráfego. Padrão: chamar em <c>Program.cs</c> entre <c>builder.Build()</c>
/// e <c>app.Run()</c>.
///
/// Falha de AWS catastrófica (timeout, AccessDenied global) propaga e bloqueia o boot
/// — política operacional: app não sobe sem secret manager. Falhas individuais de
/// secret não derrubam (já tratadas como warning dentro do preloader).
/// </summary>
public static class RuntimeSecretStoreActivator
{
    /// <summary>
    /// Resolve e popula o <see cref="IRuntimeSecretStore"/>. Substitui qualquer
    /// registro anterior (cenários de teste podem injetar store fake antes).
    /// </summary>
    public static async Task PreloadAndRegisterAsync(
        IServiceProvider rootProvider,
        CancellationToken ct = default)
    {
        var logger = rootProvider.GetRequiredService<ILogger<SecretsPreloader>>();
        var aws = rootProvider.GetRequiredService<IAmazonSecretsManager>();
        var bootstrapMap = rootProvider.GetService<AwsSecretsBootstrapExtensions.BootstrapReferenceMap>()
            ?? new AwsSecretsBootstrapExtensions.BootstrapReferenceMap(
                new Dictionary<string, string?>(StringComparer.Ordinal));

        // Cria um scope efêmero pra resolver os sources — alguns dependem de
        // scoped (DbContext, repositórios). Sources fazem leitura única no boot;
        // após esse await, o scope é descartado.
        await using var scope = rootProvider.CreateAsyncScope();
        var sources = scope.ServiceProvider
            .GetServices<ISecretReferenceSource>()
            .ToList();

        var preloader = new SecretsPreloader(aws, logger, bootstrapMap.References, sources);
        var resolved = await preloader.LoadAsync(ct);

        var storeLogger = rootProvider.GetRequiredService<ILogger<RuntimeSecretStore>>();
        var store = new RuntimeSecretStore(resolved, storeLogger);

        var host = rootProvider.GetRequiredService<MutableRuntimeSecretStoreHost>();
        host.SetStore(store);
    }
}

/// <summary>
/// Wrapper Singleton registrado no DI antes do <c>builder.Build()</c>. Backing field
/// trocado pelo <see cref="RuntimeSecretStoreActivator"/> ao fim do boot. Reads
/// antes do preload completar lançam — não há request HTTP nessa janela
/// (preload acontece antes de <c>app.Run()</c>).
/// </summary>
public sealed class MutableRuntimeSecretStoreHost : IRuntimeSecretStore
{
    private volatile IRuntimeSecretStore? _store;

    public void SetStore(IRuntimeSecretStore store)
    {
        // Set único — ativador é chamado uma vez no boot. Se chamado em retry
        // (testes/dev), aceita substituição idempotente.
        _store = store;
    }

    public int LoadedCount => Resolve().LoadedCount;

    public string? Get(string? referenceOrLiteral) => Resolve().Get(referenceOrLiteral);

    private IRuntimeSecretStore Resolve() =>
        _store ?? throw new InvalidOperationException(
            "RuntimeSecretStore não foi inicializado. " +
            "Chame RuntimeSecretStoreActivator.PreloadAndRegisterAsync no boot antes de servir tráfego.");
}
