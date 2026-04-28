namespace EfsAiHub.Infra.Secrets.Options;

public sealed class AwsSecretsOptions
{
    public const string SectionName = "Secrets:Aws";

    public string? Region { get; set; }

    public int RetryAttempts { get; set; } = 3;

    public int L1TtlSeconds { get; set; } = 60;

    public int L2TtlSeconds { get; set; } = 300;

    public int L1MaxEntries { get; set; } = 500;

    public bool EnableLegacyDpapi { get; set; } = true;

    public string CacheKeyPrefix { get; set; } = "secret:";

    public string? HealthCheckCanaryReference { get; set; }
}
