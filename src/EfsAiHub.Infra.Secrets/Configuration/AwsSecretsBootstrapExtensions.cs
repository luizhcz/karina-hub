using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Secrets.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Infra.Secrets.Configuration;

public static class AwsSecretsBootstrapExtensions
{
    public const string BootstrapSection = "Secrets:Bootstrap";

    /// <summary>
    /// Conjunto de referências originais lidas de <c>Secrets:Bootstrap</c>
    /// antes da resolução. <see cref="SecretsPreloader"/> consome isto pra
    /// agregar os identificadores AWS junto com os de projects/agents no
    /// preload único do runtime store.
    /// </summary>
    public sealed record BootstrapReferenceMap(IReadOnlyDictionary<string, string?> References);

    /// <summary>
    /// Resolve sincronamente todas as referências em <c>Secrets:Bootstrap</c> contra
    /// o AWS Secrets Manager e injeta os valores em <see cref="IConfigurationBuilder"/>
    /// como uma fonte in-memory (sobrescreve placeholders existentes). Fail-fast em
    /// qualquer falha — o app não sobe se uma referência crítica não resolve.
    ///
    /// Retorna o mapa original (config-key → referência crua) pra ser registrado
    /// como <see cref="BootstrapReferenceMap"/> no DI e consumido pelo preloader
    /// runtime.
    /// </summary>
    public static BootstrapReferenceMap AddAwsSecretsBootstrap(
        this IConfigurationManager manager,
        IAmazonSecretsManager? client = null)
    {
        var bootstrapSection = manager.GetSection(BootstrapSection);
        // AsEnumerable retorna todas as leaves (incluindo nested colon-separated)
        // com paths relativos à seção. Filtra placeholders vazios.
        var entries = bootstrapSection.AsEnumerable(makePathsRelative: true)
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToArray();

        var originalRefs = entries.ToDictionary(
            kv => kv.Key,
            kv => (string?)kv.Value,
            StringComparer.Ordinal);

        if (entries.Length == 0)
            return new BootstrapReferenceMap(originalRefs);

        var awsOptions = new AwsSecretsOptions();
        manager.GetSection(AwsSecretsOptions.SectionName).Bind(awsOptions);

        var ownsClient = client is null;
        client ??= CreateClient(awsOptions);

        try
        {
            var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var (configKey, refValue) in entries)
            {
                var reference = SecretReference.Parse(refValue);
                if (reference is not AwsSecretReference aws)
                {
                    throw new InvalidOperationException(
                        $"Bootstrap entry '{configKey}' must be an AWS Secrets Manager reference " +
                        $"(prefix '{SecretReference.AwsPrefix}'). Got: '{refValue}'.");
                }

                try
                {
                    var response = client.GetSecretValueAsync(
                            new GetSecretValueRequest { SecretId = aws.Identifier })
                        .GetAwaiter().GetResult();

                    resolved[configKey] = response.SecretString;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to resolve bootstrap secret '{configKey}' (reference '{refValue}'). " +
                        $"Verify the AWS reference exists and the IAM principal has GetSecretValue permission.",
                        ex);
                }
            }

            manager.AddInMemoryCollection(resolved);
            return new BootstrapReferenceMap(originalRefs);
        }
        finally
        {
            if (ownsClient)
                client.Dispose();
        }
    }

    private static IAmazonSecretsManager CreateClient(AwsSecretsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Region))
        {
            var region = RegionEndpoint.GetBySystemName(options.Region);
            return new AmazonSecretsManagerClient(region);
        }
        return new AmazonSecretsManagerClient();
    }

    /// <summary>
    /// Registra o cliente AWS (Singleton) + a <see cref="BootstrapReferenceMap"/>
    /// resolvida no AddAwsSecretsBootstrap. O <see cref="IRuntimeSecretStore"/>
    /// é populado posteriormente, no fim do boot, via
    /// <see cref="RuntimeSecretStoreActivator.PreloadAndRegisterAsync"/>.
    /// </summary>
    public static IServiceCollection AddAwsSecretsManager(
        this IServiceCollection services,
        IConfiguration configuration,
        BootstrapReferenceMap? bootstrapReferences = null)
    {
        services.Configure<AwsSecretsOptions>(configuration.GetSection(AwsSecretsOptions.SectionName));

        services.AddSingleton<IAmazonSecretsManager>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AwsSecretsOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.Region))
            {
                var region = RegionEndpoint.GetBySystemName(opts.Region);
                return new AmazonSecretsManagerClient(region);
            }
            return new AmazonSecretsManagerClient();
        });

        if (bootstrapReferences is not null)
            services.AddSingleton(bootstrapReferences);

        return services;
    }
}
