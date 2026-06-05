namespace EfsAiHub.Platform.Runtime.Tools;

/// <summary>
/// Config das APIs externas consumidas por <see cref="PortfolioAnalysisTool"/>.
/// Bound à seção <c>PortfolioApi</c> do appsettings. <see cref="BaseUrl"/> vazio
/// significa "tool não está pronta pra HTTP real" — qualquer invocação de
/// <c>analyze_portfolio</c> nesse estado dispara <see cref="System.InvalidOperationException"/>
/// orientando a preencher a config.
/// </summary>
public sealed class PortfolioApiOptions
{
    public const string SectionName = "PortfolioApi";

    /// <summary>
    /// Base URL do provedor das APIs de posições e recomendações.
    /// Ex.: <c>https://positions.efs.internal</c>. Os paths abaixo são
    /// concatenados a este valor.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path relativo do endpoint de posições. Default placeholder.</summary>
    public string PositionsPath { get; set; } = "/positions";

    /// <summary>Path relativo do endpoint de recomendações. Default placeholder.</summary>
    public string RecommendationsPath { get; set; } = "/assets/recommendations";

    /// <summary>Timeout por chamada HTTP, em segundos. Aplicado via CTS no caller.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
