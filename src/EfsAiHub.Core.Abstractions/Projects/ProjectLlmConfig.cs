namespace EfsAiHub.Core.Abstractions.Projects;

/// <summary>
/// Configuração LLM por projeto: credenciais por provider e defaults de modelo.
/// As ApiKeys são armazenadas cifradas no banco (via IDataProtector) e
/// descriptografadas pelo repositório antes de retornar ao domínio.
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
/// Credenciais de um provider específico para um projeto.
/// ApiKey contém o valor em plaintext no domínio; a persistência cuida da cifragem.
/// </summary>
public sealed record ProviderCredentials
{
    /// <summary>API Key em plaintext. Null = usar credencial global do appsettings.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Endpoint customizado. Null = usar endpoint global do appsettings.</summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Timestamp de quando a ApiKey foi cifrada — usado para rastrear rotação de keys.
    /// Preenchido automaticamente pelo repositório ao persistir.
    /// </summary>
    public string? KeyVersion { get; init; }
}
