namespace EfsAiHub.Infra.LlmProviders.Configuration;

public class AzureAIOptions
{
    public const string SectionName = "AzureAI";

    public required string Endpoint { get; init; }

    /// <summary>Null = usa DefaultAzureCredential (recomendado)</summary>
    public string? ApiKey { get; init; }

    public required string DefaultDeploymentName { get; init; }
}
