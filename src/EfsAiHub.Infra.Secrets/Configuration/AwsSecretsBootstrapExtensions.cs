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
    /// Resolve sincronamente todas as referências em <c>Secrets:Bootstrap</c> contra
    /// o AWS Secrets Manager e injeta os valores em <see cref="IConfigurationBuilder"/>
    /// como uma fonte in-memory (sobrescreve placeholders existentes). Fail-fast em
    /// qualquer falha — o app não sobe se uma referência crítica não resolve.
    /// </summary>
    public static IConfigurationBuilder AddAwsSecretsBootstrap(
        this IConfigurationManager manager,
        IAmazonSecretsManager? client = null)
    {
        var bootstrapSection = manager.GetSection(BootstrapSection);
        // AsEnumerable retorna todas as leaves (incluindo nested colon-separated)
        // com paths relativos à seção. Filtra placeholders vazios.
        var entries = bootstrapSection.AsEnumerable(makePathsRelative: true)
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToArray();

        if (entries.Length == 0)
            return manager;

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
            return manager;
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

    public static IServiceCollection AddAwsSecretsManager(
        this IServiceCollection services,
        IConfiguration configuration)
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

        services.AddSingleton<ISecretCacheService, SecretCacheService>();
        services.AddSingleton<ISecretResolver, AwsSecretsManagerResolver>();

        return services;
    }
}
