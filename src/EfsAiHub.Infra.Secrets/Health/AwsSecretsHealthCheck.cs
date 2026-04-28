using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Secrets.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.Secrets.Health;

public sealed class AwsSecretsHealthCheck : IHealthCheck
{
    private readonly IAmazonSecretsManager _client;
    private readonly AwsSecretsOptions _options;

    public AwsSecretsHealthCheck(IAmazonSecretsManager client, IOptions<AwsSecretsOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.HealthCheckCanaryReference))
            return HealthCheckResult.Healthy("AWS Secrets Manager: no canary configured.");

        var reference = SecretReference.Parse(_options.HealthCheckCanaryReference);
        if (reference is not AwsSecretReference aws)
        {
            return HealthCheckResult.Degraded(
                $"Canary reference '{_options.HealthCheckCanaryReference}' is not a valid AWS reference.");
        }

        try
        {
            await _client.DescribeSecretAsync(
                new DescribeSecretRequest { SecretId = aws.Identifier },
                cancellationToken);
            return HealthCheckResult.Healthy("AWS Secrets Manager reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("AWS Secrets Manager canary failed.", ex);
        }
    }
}
