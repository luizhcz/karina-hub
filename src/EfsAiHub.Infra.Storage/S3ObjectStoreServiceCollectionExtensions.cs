using Amazon;
using Amazon.S3;
using EfsAiHub.Core.Abstractions.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Infra.Storage;

public static class S3ObjectStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registra o object store. Com <c>Storage:S3:Enabled=false</c> (default)
    /// registra um <see cref="NoOpObjectStore"/> — não exige bucket nem
    /// credenciais, ideal pra dev/ambientes sem S3 ligado. Com Enabled=true
    /// registra <see cref="IAmazonS3"/> (credenciais via default chain, igual ao
    /// Secrets Manager) + <see cref="S3ObjectStore"/>.
    /// </summary>
    public static IServiceCollection AddS3ObjectStore(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(S3ObjectStoreOptions.SectionName);
        services.Configure<S3ObjectStoreOptions>(section);
        var opts = section.Get<S3ObjectStoreOptions>() ?? new S3ObjectStoreOptions();

        if (!opts.Enabled)
        {
            services.AddSingleton<IObjectStore, NoOpObjectStore>();
            return services;
        }

        // Fail-fast de MISCONFIG com mensagem clara: sem isto, o BucketName vazio só
        // estoura tarde, no ctor do S3ObjectStore durante a construção do HostedService,
        // derrubando o host inteiro com stack opaco (resolução de IHostedService).
        if (string.IsNullOrWhiteSpace(opts.BucketName))
            throw new InvalidOperationException(
                "Storage:S3:Enabled=true exige Storage:S3:BucketName preenchido.");

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new AmazonS3Config
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds)),
            };

            if (!string.IsNullOrWhiteSpace(opts.ServiceUrl))
            {
                // MinIO/LocalStack em dev.
                config.ServiceURL = opts.ServiceUrl;
                config.ForcePathStyle = opts.ForcePathStyle;
            }
            else if (!string.IsNullOrWhiteSpace(opts.Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(opts.Region);
            }

            // Sem credenciais explícitas: default credential chain (IAM role em
            // prod, AWS_PROFILE + ~/.aws em dev), mesma cadeia do Secrets Manager.
            return new AmazonS3Client(config);
        });

        services.AddSingleton<IObjectStore, S3ObjectStore>();
        return services;
    }
}
