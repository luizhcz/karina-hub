namespace EfsAiHub.Infra.LlmProviders.Personas.Options;

/// <summary>
/// Config da API externa que fornece <see cref="Core.Abstractions.Identity.Persona.UserPersona"/>.
/// Seção <c>Persona</c> no appsettings.
///
/// TimeoutSeconds agressivo (3s) é intencional: a chamada fica no hot path
/// do chat, então falha rápida + fallback Anonymous é melhor que contaminar p95.
/// </summary>
public sealed class PersonaApiOptions
{
    public const string SectionName = "Persona";

    public string BaseUrl { get; init; } = "";
    public string? ApiKey { get; init; }
    public string AuthScheme { get; init; } = "Bearer";

    /// <summary>
    /// Path relativo ao <see cref="BaseUrl"/> para resolver um cliente. Receberá
    /// <c>{userId}</c> apenso no final. Default espelha convenção REST — se a API
    /// real usar outro caminho, sobrescrever via config (<c>Persona:ClientPath</c>).
    /// </summary>
    public string ClientPath { get; init; } = "personas/clientes";

    /// <summary>
    /// Path relativo ao <see cref="BaseUrl"/> para resolver um admin
    /// (assessor/gestor/consultor/padrão). Default: <c>personas/admins</c>.
    /// </summary>
    public string AdminPath { get; init; } = "personas/admins";

    /// <summary>Timeout por chamada HTTP. Default 3s — não inflar.</summary>
    public int TimeoutSeconds { get; init; } = 3;

    /// <summary>TTL da entrada no Redis (cache L2 cross-pod).</summary>
    public int CacheTtlMinutes { get; init; } = 5;

    /// <summary>TTL do cache local in-memory L1 (hot path).</summary>
    public int LocalCacheTtlSeconds { get; init; } = 60;

    /// <summary>
    /// true desliga o provider completamente — qualquer chamada retorna
    /// <c>UserPersona.Anonymous</c> sem tocar na rede. Útil em ambientes
    /// onde a API ainda não está disponível.
    /// </summary>
    public bool Disabled { get; init; } = false;
}
