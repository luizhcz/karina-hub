namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// F6 — experiment A/B de templates. Permite rodar dois VersionIds de um
/// mesmo <see cref="PersonaPromptTemplate"/> em paralelo com split de tráfego
/// configurável.
///
/// Design:
/// <list type="bullet">
///   <item>Scope mesma string que template (<c>project:{pid}:*</c>,
///     <c>agent:{aid}:*</c>, <c>global:*</c>). Composer lookup troca a
///     resolução normal pela busca de experiment ativo pra (ProjectId, Scope).</item>
///   <item><see cref="VariantAVersionId"/> / <see cref="VariantBVersionId"/>
///     apontam pra <see cref="PersonaPromptTemplateVersion.VersionId"/> —
///     snapshots imutáveis. Rollback do template não afeta experiment.</item>
///   <item><see cref="TrafficSplitB"/> é % (0-100) do tráfego que recebe B.</item>
///   <item>Bucketing determinístico por <c>userId</c>: mesma chamada sempre
///     vai pra mesma variant (sticky across retries).</item>
/// </list>
///
/// Isolamento: <see cref="ProjectId"/> é o boundary (ADR 003). UNIQUE parcial
/// (onde <see cref="EndedAt"/> IS NULL) garante 1 experiment ativo por
/// (ProjectId, Scope).
/// </summary>
public sealed class PersonaPromptExperiment
{
    public int Id { get; init; }

    /// <summary>Project dono do experiment (ADR 003).</summary>
    public required string ProjectId { get; init; }

    /// <summary>Scope de template alvo (ex: <c>project:p1:cliente</c>).</summary>
    public required string Scope { get; set; }

    /// <summary>Nome legível pra UI admin.</summary>
    public required string Name { get; set; }

    public required Guid VariantAVersionId { get; set; }
    public required Guid VariantBVersionId { get; set; }

    /// <summary>% de tráfego pra variant B (0-100). A = 100 - B.</summary>
    public required int TrafficSplitB { get; set; }

    /// <summary>
    /// Semantic hint (cost_usd, total_tokens, hitl_approved). Backend
    /// não enforce — só armazena. UI agrega conforme.
    /// </summary>
    public required string Metric { get; set; }

    public DateTime StartedAt { get; init; }

    /// <summary>Null = em curso. Set = encerrado.</summary>
    public DateTime? EndedAt { get; set; }

    public string? CreatedBy { get; init; }

    public bool IsActive => EndedAt is null;
}

/// <summary>
/// F6 — resultado de assignment. Propagado via ExecutionContext pro
/// <see cref="IPersonaPromptComposer"/> usar a version snapshot correta e
/// pro writer de llm_token_usage persistir variant.
/// </summary>
public sealed record ExperimentAssignment(
    int ExperimentId,
    char Variant,
    Guid TemplateVersionId)
{
    public static char AssignVariant(string userId, int experimentId, int trafficSplitB)
    {
        // Bucketing determinístico: SHA256(userId + experimentId) % 100.
        // Usa os primeiros 4 bytes como uint pra evitar viés de módulo em
        // range pequeno (uint [0, 4B] % 100 é uniforme o suficiente).
        var key = $"{userId}|{experimentId}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var bucket = BitConverter.ToUInt32(hash, 0) % 100u;
        return bucket < (uint)trafficSplitB ? 'B' : 'A';
    }
}
