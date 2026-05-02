namespace EfsAiHub.Platform.Runtime.Configuration;

/// <summary>
/// Limites globais para Generic Tools (HTTP genéricas cadastradas por projeto).
/// <c>DefaultTimeoutSeconds</c> aplica quando o tool não tem override próprio;
/// <c>MaxTimeoutSeconds</c> é o teto absoluto — overrides per-tool são validados
/// no service via <c>GenericTool.EnsureWithinTimeoutCeiling</c>.
/// </summary>
public class GenericToolsOptions
{
    public const string SectionName = "GenericTools";

    public int DefaultTimeoutSeconds { get; init; } = 120;
    public int MaxTimeoutSeconds { get; init; } = 120;
}
