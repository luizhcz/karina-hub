namespace EfsAiHub.Platform.Runtime.Configuration;

/// <summary>
/// Configuração da feature de ingestão URL→arquivo→workflow. Bound à seção
/// <c>IngestionApi</c> do appsettings. Quando <see cref="Enabled"/> é false,
/// o endpoint <c>POST /api/aihub/ingestions</c> responde 503.
/// </summary>
public sealed class IngestionApiOptions
{
    public const string SectionName = "IngestionApi";

    /// <summary>Liga/desliga o endpoint. Default <c>false</c> — feature flag.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Timeout por download HTTP, em segundos. Default 120.</summary>
    public int DownloadTimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Limite de redirects HTTP seguidos manualmente. Default 2 — pequeno
    /// pra evitar redirect chains maliciosas.
    /// </summary>
    public int MaxRedirects { get; init; } = 2;

    /// <summary>
    /// Tamanho máximo do arquivo em bytes. Default 100 MB — soft cap defensivo
    /// alinhado com <c>DocumentIntelligenceOptions.MaxFileSizeBytes</c>. Quando
    /// &gt; 0, download é abortado mid-stream ao ultrapassar. <c>0</c> = sem
    /// limite (opt-in explícito, risco real de OOM via Transfer-Encoding chunked
    /// maliciosa — habilitar apenas em ambientes confiáveis).
    /// </summary>
    public long MaxSizeBytes { get; init; } = 100L * 1024 * 1024;
}
