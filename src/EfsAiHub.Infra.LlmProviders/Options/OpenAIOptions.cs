namespace EfsAiHub.Infra.LlmProviders.Configuration;

public class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    /// <summary>
    /// Chave de API da OpenAI. Configure em appsettings.Development.json ou via variável de ambiente.
    /// Usada como fallback quando o AgentDefinition não inclui 'provider.apiKey'.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Modelo padrão para o provider OpenAI.
    /// Usado quando AgentDefinition.Model.DeploymentName está vazio.
    /// </summary>
    public string DefaultModel { get; init; } = "gpt-4o";
}
