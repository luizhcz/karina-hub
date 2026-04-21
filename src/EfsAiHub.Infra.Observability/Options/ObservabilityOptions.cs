namespace EfsAiHub.Infra.Observability.Configuration;

public class ObservabilityOptions
{
    public const string SectionName = "OpenTelemetry";

    public string ServiceName { get; init; } = "EfsAiHub.Api";
    public string? OtlpEndpoint { get; init; }
    public bool EnableSensitiveData { get; init; } = false;
}
