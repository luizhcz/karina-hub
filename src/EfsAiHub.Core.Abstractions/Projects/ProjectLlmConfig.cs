namespace EfsAiHub.Core.Abstractions.Projects;

/// <summary>
/// Configuração LLM por projeto: referências AWS Secrets Manager por provider
/// e defaults de modelo. ApiKey carrega a referência (`secret://aws/...`); a
/// resolução do valor real acontece em runtime via ISecretResolver.
/// </summary>
public sealed record ProjectLlmConfig
{
    /// <summary>
    /// Credenciais por provider. Chave = provider type em maiúsculas
    /// (ex: "OPENAI", "AZUREOPENAI", "AZUREFOUNDRY").
    /// </summary>
    public Dictionary<string, ProviderCredentials> Credentials { get; init; } = new();

    /// <summary>Modelo padrão do projeto (ex: "gpt-4o").</summary>
    public string? DefaultModel { get; init; }

    /// <summary>Provider padrão do projeto (ex: "OPENAI").</summary>
    public string? DefaultProvider { get; init; }
}

/// <summary>
/// Credenciais de um provider específico para um projeto. ApiKey carrega a
/// referência AWS Secrets Manager (formato `secret://aws/...`). O valor é
/// resolvido em runtime pelo ISecretResolver.
/// </summary>
public sealed record ProviderCredentials
{
    /// <summary>Referência AWS Secrets Manager. Null = usar credencial global do appsettings.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Endpoint customizado. Null = usar endpoint global do appsettings.</summary>
    public string? Endpoint { get; init; }
}
