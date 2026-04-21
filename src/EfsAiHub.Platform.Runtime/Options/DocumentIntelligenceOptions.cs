namespace EfsAiHub.Platform.Runtime.Options;

/// <summary>
/// Configuração do executor Document Intelligence.
/// Seção: "DocumentIntelligence" no appsettings.json.
/// </summary>
public class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    public string Endpoint { get; init; } = "";
    public bool UseManagedIdentity { get; init; } = true;
    public string? ApiKey { get; init; }
    public string DefaultModel { get; init; } = "prebuilt-layout";
    public int MaxPages { get; init; } = 100;
    public int PollingTimeoutSeconds { get; init; } = 180;
    public int GateWaitTimeoutSeconds { get; init; } = 600;
    public int CacheTtlDays { get; init; } = 7;
}
