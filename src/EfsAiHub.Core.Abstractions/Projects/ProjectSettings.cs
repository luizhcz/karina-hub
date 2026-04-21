namespace EfsAiHub.Core.Abstractions.Projects;

/// <summary>
/// Configurações de um projeto. Serializado como JSONB na coluna settings.
/// Inclui SchemaVersion para migração futura de settings antigos (S3).
/// </summary>
public sealed record ProjectSettings
{
    /// <summary>Versão do schema — permite migração de settings antigos sem NullReference.</summary>
    public int SchemaVersion { get; init; } = 1;

    // ── LLM Defaults ──
    public string? DefaultProvider { get; init; }
    public string? DefaultModel { get; init; }
    public float? DefaultTemperature { get; init; }

    // ── Limites de custo/uso ──
    public int? MaxTokensPerDay { get; init; }
    public decimal? MaxCostUsdPerDay { get; init; }

    // ── Limites de concorrência ──
    public int? MaxConcurrentExecutions { get; init; }
    public int? MaxRequestsPerMinute { get; init; }
    public int? MaxConversationsPerUser { get; init; }

    // ── Feature flags ──
    public bool HitlEnabled { get; init; } = true;
    public bool BackgroundResponsesEnabled { get; init; } = true;

    // ── Sandbox ── (R4: budget de sandbox)
    public int? MaxSandboxTokensPerDay { get; init; } = 50_000;

    /// <summary>
    /// Migra settings de versões anteriores para a versão atual.
    /// Chamado pelo repositório ao deserializar do banco.
    /// </summary>
    public ProjectSettings Migrate()
    {
        return SchemaVersion switch
        {
            1 => this,
            _ => this
        };
    }
}
